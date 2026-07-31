# Inline Images Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user put an image into the message body by pasting it or dropping it there, and pick one of three display widths for it.

**Architecture:** The outbound half already exists — `OutgoingMessageFactory` rewrites a staged content URL into a `cid:` and packs the part as a linked resource. This slice adds the producer: the staging endpoint learns to assign a Content-ID, and the composer learns two gestures and a size bar. A new `.compose-body` wrapper inside `ComposeView` is the element that owns the gestures, so `SquireEditor` stays a thin shell over Squire.

**Tech Stack:** ASP.NET Core 10 / MimeKit / xUnit + Moq on the backend; React 18 + TypeScript, Squire (`squire-rte`), Vitest + jsdom + @testing-library/react on the frontend.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-31-webmail-inline-images-design.md`.
- **No toolbar button.** Insertion is paste and drop only. Do not add one "for discoverability".
- **The bytes always leave whole.** Nothing is re-encoded or resampled anywhere in this plan.
- **An inline image never enters the attachment tray** while it is in the body. It only lands there through the existing plain-text `adoptInline` path.
- `dotnet test` (not `--no-build`) whenever a test file is added.
- `src/snoopy.microservice/ApiDocumentation.xml` is a versioned artefact that `dotnet test` regenerates with hundreds of unrelated lines. **Revert it before every commit**: `git checkout -- src/snoopy.microservice/ApiDocumentation.xml`.
- A commit message may not begin or end with `@`.
- Frontend commands run from `src/frontend`.

---

### Task 1: The staging endpoint assigns a Content-ID

`POST /api/Mail/Attachments` gains an `inline` form field. When set, the controller generates a Content-ID and refuses anything that is not an image — an inline part that cannot be displayed would ride in the `multipart/related` referenced by nothing.

`IStagedAttachmentStore.SaveAsync` already takes an optional `contentId` (`StagedAttachmentStore.cs:39`) and `StagedAttachmentInfo` already carries `ContentId`, so neither the store nor the response shape moves.

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailController.cs:1-9` (using), `:820-857` (the action)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs:214-229`

**Interfaces:**
- Consumes: `IStagedAttachmentStore.SaveAsync(string accountId, string fileName, string contentType, Stream content, CancellationToken cancellationToken, string? contentId = null)`
- Produces: `UploadAttachment(IFormFile? file, bool inline, CancellationToken cancellationToken)` — the parameter order later call sites (and the existing test at line 225) must use.

- [ ] **Step 1: Write the two failing tests**

Add to `MailControllerTests.cs`, directly after `UploadAttachment_StagesUnderTheActiveAccount` (line 229). Leave that existing test as it stands — its `Verify` expression omits `contentId`, which in an expression tree means "the default", so it already claims that an ordinary upload stages no Content-ID.

```csharp
    // An inline part is referenced from the body by cid; without an id assigned here the composer
    // could only ever produce attachments, whatever the body says.
    [Fact]
    public async Task UploadAttachment_AssignsAContentIdToAnInlineImage()
    {
        var controller = CreateController();
        ResolveTo(ConnectedConn);
        _staged.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
            .ReturnsAsync(Result.Success(new StagedAttachmentInfo(Guid.NewGuid(), "shot.png", 4, "image/png")));
        var file = new FormFile(new MemoryStream("abcd"u8.ToArray()), 0, 4, "file", "shot.png")
        { Headers = new HeaderDictionary(), ContentType = "image/png" };

        await controller.UploadAttachment(file, inline: true, CancellationToken.None);

        _staged.Verify(s => s.SaveAsync(ConnectedConn.StagedScope(controller.AuthenticatedUser),
            "shot.png", "image/png", It.IsAny<Stream>(), It.IsAny<CancellationToken>(),
            It.Is<string>(id => !string.IsNullOrWhiteSpace(id))), Times.Once);
    }

    // A non-image inline part has nowhere to be shown: it would travel in the related part
    // referenced by nothing, which is the condition the send path's pruning exists to prevent.
    [Fact]
    public async Task UploadAttachment_RefusesANonImageInlinePart()
    {
        var controller = CreateController();
        ResolveTo(ConnectedConn);
        var file = new FormFile(new MemoryStream("abcd"u8.ToArray()), 0, 4, "file", "a.pdf")
        { Headers = new HeaderDictionary(), ContentType = "application/pdf" };

        var result = await controller.UploadAttachment(file, inline: true, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _staged.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
    }
```

Then update the one existing call site, `MailControllerTests.cs:225`, to the new signature:

```csharp
        await controller.UploadAttachment(file, inline: false, CancellationToken.None);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/snoopy.microservice --filter UploadAttachment`
Expected: compile error — `UploadAttachment` takes no `inline` argument.

- [ ] **Step 3: Implement**

In `MailController.cs`, add the using for `MimeUtils` after line 3 (`using MimeKit;`):

```csharp
using MimeKit.Utils;
```

Replace the action body at `:843-857`:

```csharp
    public async Task<ActionResult<StagedAttachmentInfo>> UploadAttachment(
        IFormFile? file, [FromForm] bool inline, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return BadRequestEnveloppe("A file is required");
        // Beside the file check rather than after the account resolution: both describe the
        // request itself, and neither needs a mailbox to be judged.
        if (inline && !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequestEnveloppe("An inline part must be an image");

        var (connection, error) = await TryResolveAsync(cancellationToken);
        if (connection is null) return error!;

        await using var content = file.OpenReadStream();
        var result = await staged.SaveAsync(
            connection.StagedScope(AuthenticatedUser),
            file.FileName, file.ContentType, content, cancellationToken,
            inline ? MimeUtils.GenerateMessageId() : null);

        return FromResult(result);
    }
```

Add the two response lines to the doc comment above it, after the `<param name="file">` line:

```csharp
    /// <param name="inline">stage as a body resource (cid) rather than an attachment</param>
```

and widen the 400 description at `:831`:

```csharp
    /// <response code="400">No file, a non-image staged inline, file over the limit, or account staging cap reached</response>
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/snoopy.microservice --filter UploadAttachment`
Expected: PASS, 3 tests.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test src/snoopy.microservice`
Expected: PASS. Nothing else calls `UploadAttachment`.

- [ ] **Step 6: Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Controllers/MailController.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs
git commit -F - <<'EOF'
Stage an attachment as an inline body resource

An inline upload gets a generated Content-ID; a non-image is refused, since it
could only travel referenced by nothing.
EOF
```

---

### Task 2: The editor can insert an image

`SquireEditor`'s handle gains `insertImage`. Nothing else about the component changes — the gestures live in `ComposeView` (Task 3), because the wrapper element it owns is what can tell "over the body" from "over the composer".

The `max-width: 100%` written here is the one that survives to the recipient: Ganss's defaults keep both `style` and `width` on the way out (`OutgoingMailSanitizer.cs:22-31`), and the sizing bar in Task 4 only adds the `width` attribute beside it.

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/SquireEditor.tsx:13-24` (the handle type), `:104-138` (the implementation)
- Test: `src/frontend/src/modules/mail/compose/SquireEditor.mount.test.tsx`

**Interfaces:**
- Produces: `EditorHandle.insertImage(src: string): void` — used by `ComposeView` in Task 3.

- [ ] **Step 1: Write the failing test**

Append inside the existing `describe` in `SquireEditor.mount.test.tsx`:

```tsx
  // Against the real engine, not a mock: an insertImage signature that has moved is exactly the
  // class of mismatch the sibling suite's stub cannot see.
  it('inserts an image bounded to the message width', () => {
    const ref = createRef<EditorHandle>()
    const view = render(<SquireEditor ref={ref} onChange={() => {}} />)

    ref.current!.insertImage('https://api.test.example/api/Mail/Attachments/i1/content')

    const html = ref.current!.getHTML()
    expect(html).toContain('src="https://api.test.example/api/Mail/Attachments/i1/content"')
    expect(html).toContain('max-width')
    expect(ref.current!.isEmpty()).toBe(false)
    view.unmount()
  })
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/SquireEditor.mount.test.tsx`
Expected: FAIL — `ref.current.insertImage is not a function`.

- [ ] **Step 3: Implement**

In `SquireEditor.tsx`, add to the `EditorHandle` interface after `makeLink` (line 23):

```ts
  insertImage: (src: string) => void
```

and to the `useImperativeHandle` object after the `makeLink` entry (line 137):

```ts
    // The bound is on the image itself rather than in a stylesheet: the recipient's client has
    // none of ours, and a 4000px photo would otherwise blow out their reading column.
    insertImage: (src) => { editor.current?.insertImage(src, { style: 'max-width: 100%' }) },
```

- [ ] **Step 4: Run the editor suites**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/SquireEditor.mount.test.tsx src/modules/mail/compose/SquireEditor.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/compose/SquireEditor.tsx src/frontend/src/modules/mail/compose/SquireEditor.mount.test.tsx
git commit -F - <<'EOF'
Let the editor insert an image

One handle method over Squire's insertImage, bounding the image to the message
width so a full-size photo does not overrun the recipient's column.
EOF
```

---

### Task 3: Paste and drop put an image in the body

The whole insertion path, plus the bookkeeping that keeps a staged id from leaking. Four things move together here because none of them is testable without the others: the API client learns the flag, the hook learns to hold an imperatively added inline id, `ComposeView` gains the `.compose-body` wrapper that owns the two gestures, and the drop overlay stops covering its own target.

**`.compose-drop-overlay` is `inset: 0; z-index: 30` with no `pointer-events` rule** (`mail.css:1472`). As soon as a drag starts it covers the editor, so without the CSS line in Step 7 the body can never receive `dragenter` or `drop` and this task's browser check fails while every test passes.

**Files:**
- Modify: `src/frontend/src/api.js:415-456` (`uploadAttachment`)
- Modify: `src/frontend/src/modules/mail/compose/useStagedAttachments.ts:23-131`
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx` (imports, drag state, the render around `:392-399` and `:453-455`)
- Modify: `src/frontend/src/styles/mail.css:1318-1326` (`.compose-editor` neighbourhood), `:1472-1478` (the overlay)
- Test: `src/frontend/src/modules/mail/compose/useStagedAttachments.test.tsx`, `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`

**Interfaces:**
- Consumes: `EditorHandle.insertImage(src)` (Task 2); `stagedAttachmentUrl(id, accountId)`, `uploadAttachment(file, options)` from `api.js`.
- Produces: `useStagedAttachments(...).addInline(id: string): void`; `uploadAttachment(file, { onProgress?, signal?, accountId?, inline? })`.

- [ ] **Step 1: Write the failing hook test**

Append to `useStagedAttachments.test.tsx`:

```tsx
  // An image inserted then abandoned in the same breath must still be released: routed through
  // the seed prop it would not be recorded until the next effect flush, and the bytes would fall
  // to the TTL sweeper in silence.
  it('releases an imperatively added inline id on discard', () => {
    const { result } = renderHook(() => useStagedAttachments('primary', [], []))

    act(() => { result.current.addInline('i9') })
    act(() => { result.current.discardAll() })

    expect(mocks.deleteAttachment).toHaveBeenCalledWith('i9', { accountId: 'primary' })
  })

  it('moves an imperatively added inline id into the tray on adoption', () => {
    const { result } = renderHook(() => useStagedAttachments('primary', [], []))

    act(() => { result.current.addInline('i9') })
    act(() => { result.current.adoptInline([{ id: 'i9', fileName: 'shot.png', size: 12 }]) })

    expect(result.current.items.map(i => i.fileName)).toEqual(['shot.png'])
    expect(result.current.ids).toEqual(['i9'])
  })
```

Match the file's existing imports and `mocks` shape; if it does not already import `renderHook`/`act` from `@testing-library/react`, add them.

- [ ] **Step 2: Run it to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/useStagedAttachments.test.tsx`
Expected: FAIL — `result.current.addInline is not a function`.

- [ ] **Step 3: Implement the hook change**

In `useStagedAttachments.ts`, add a second ref beside `inlineRef` (after line 39):

```ts
  // Inline ids the composer inserted, kept apart from inlineRef: the effect below rebuilds that
  // one wholesale from the seed prop, which knows nothing about an insertion.
  const addedInlineRef = useRef<{ id: string; accountId: string }[]>([])
```

Add the setter after `release` (line 60):

```ts
  // Synchronous, like apply/itemsRef above and for the same reason: a discard can run before the
  // next passive flush, and an id it never saw is an id nobody releases.
  const addInline = useCallback((id: string) => {
    addedInlineRef.current = [...addedInlineRef.current, { id, accountId }]
  }, [accountId])
```

In `adoptInline`, replace the first two lines of the body (lines 97-98) so it drains both refs:

```ts
    const moving = [...inlineRef.current, ...addedInlineRef.current]
    if (moving.length === 0) return
    inlineRef.current = []
    addedInlineRef.current = []
```

In `discardAll`, replace the first loop (line 114):

```ts
    for (const inline of [...inlineRef.current, ...addedInlineRef.current]) release(inline.id, inline.accountId)
```

Add `addInline` to the returned object (line 127):

```ts
    items, addFiles, remove, discardAll, adoptInline, addInline,
```

- [ ] **Step 4: Run the hook test to verify it passes**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/useStagedAttachments.test.tsx`
Expected: PASS.

- [ ] **Step 5: Teach the API client the flag**

In `api.js`, change the signature at line 415 and the form assembly at line 453:

```js
export function uploadAttachment(file, { onProgress, signal, accountId, inline } = {}) {
```

```js
    const form = new FormData()
    form.append('file', file)
    if (inline) form.append('inline', 'true')
    xhr.send(form)
```

- [ ] **Step 6: Write the failing composer tests**

Append to `ComposeView.test.tsx`. First extend the `SquireEditor` stub (line 55-76) so the handle carries `insertImage`, recording into the shared `editorState`:

```tsx
const editorState = vi.hoisted(() => ({ html: '', commands: [] as string[], images: [] as string[] }))
```

and inside the stub's `useImperativeHandle` object:

```tsx
        insertImage: (src: string) => { editorState.images.push(src) },
```

Reset it wherever the suite already resets `editorState` (`editorState.html = ''`), adding `editorState.images = []`.

Then the tests:

```tsx
describe('inline images', () => {
  function imageFile(name = 'shot.png') {
    return new File(['xx'], name, { type: 'image/png' })
  }

  it('uploads a pasted image inline and inserts it at the caret', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i1', fileName: 'shot.png', size: 2 })
    renderCompose()

    fireEvent.paste(screen.getByTestId('compose-editor'), {
      clipboardData: { files: [imageFile()] },
    })

    await waitFor(() => expect(editorState.images).toEqual([
      'https://api.test.example/api/Mail/Attachments/i1/content',
    ]))
    expect(mocks.uploadAttachment).toHaveBeenCalledWith(
      expect.any(File), expect.objectContaining({ inline: true }))
    // The body is where it lives; the tray must not also list it.
    expect(screen.queryByText('shot.png')).not.toBeInTheDocument()
  })

  it('inserts an image dropped on the body and attaches one dropped on the composer', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i2', fileName: 'shot.png', size: 2 })
    renderCompose()

    fireEvent.drop(screen.getByTestId('compose-editor'), {
      dataTransfer: { files: [imageFile()], types: ['Files'] },
    })
    await waitFor(() => expect(editorState.images).toHaveLength(1))

    fireEvent.drop(screen.getByTestId('compose-view'), {
      dataTransfer: { files: [new File(['x'], 'report.pdf', { type: 'application/pdf' })], types: ['Files'] },
    })
    expect(await screen.findByText('report.pdf')).toBeInTheDocument()
    expect(editorState.images).toHaveLength(1)
  })

  it('attaches a non-image dropped on the body rather than refusing it', async () => {
    renderCompose()

    fireEvent.drop(screen.getByTestId('compose-editor'), {
      dataTransfer: { files: [new File(['x'], 'report.pdf', { type: 'application/pdf' })], types: ['Files'] },
    })

    expect(await screen.findByText('report.pdf')).toBeInTheDocument()
    expect(editorState.images).toHaveLength(0)
  })

  it('sends an inserted image as an inline id', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i3', fileName: 'shot.png', size: 2 })
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    renderCompose()
    addRecipient('To', 'her@example.com')

    fireEvent.paste(screen.getByTestId('compose-editor'), {
      clipboardData: { files: [imageFile()] },
    })
    await waitFor(() => expect(editorState.images).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.sendMessage.mock.calls[0][0].attachmentIds).toContain('i3')
  })

  it('moves an inserted image to the tray when the composer switches to plain text', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i4', fileName: 'shot.png', size: 2 })
    renderCompose()

    fireEvent.paste(screen.getByTestId('compose-editor'), {
      clipboardData: { files: [imageFile()] },
    })
    await waitFor(() => expect(editorState.images).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Plain text' }))

    expect(await screen.findByText('shot.png')).toBeInTheDocument()
  })
})
```

The last test needs no `Switch` click: `losesFormatting` reads the stub's `editorState.html`, which an insertion never writes — the stub records into `editorState.images` instead — so the confirmation does not open. If the stub is ever changed to write the `<img>` into `editorState.html`, that test gains a `fireEvent.click(screen.getByRole('button', { name: 'Switch' }))` after the toggle.

- [ ] **Step 7: Run them to verify they fail**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/ComposeView.test.tsx -t "inline images"`
Expected: FAIL — nothing handles the paste, `editorState.images` stays empty.

- [ ] **Step 8: Implement in ComposeView**

Add to the imports (beside line 24):

```tsx
import { stagedAttachmentUrl, uploadAttachment } from '../../../api.js'
```

Add the inserted-image state beside `inlineAdopted` (after line 111):

```tsx
  // Held here as well as in the hook: the hook holds them to release them, this list rides the
  // payload and names the files the tray needs if the composer ever switches to plain text.
  const [insertedInline, setInsertedInline] = useState<{ id: string; fileName: string; size: number }[]>([])
```

Replace the `inlineIds` memo (lines 112-113):

```tsx
  const inlineIds = useMemo(
    () => (inlineAdopted ? [] : [...seedInline.map(a => a.id), ...insertedInline.map(a => a.id)]),
    [seedInline, insertedInline, inlineAdopted])
```

Add the insertion path after `removeFile` (line 150):

```tsx
  const addInline = attachments.addInline
  const insertImages = useCallback(async (files: File[]) => {
    markDirty()
    for (const file of files) {
      try {
        const info = await uploadAttachment(file, { accountId, inline: true })
        addInline(info.id)
        setInsertedInline(previous => [...previous, info])
        editor?.insertImage(stagedAttachmentUrl(info.id, accountId))
      } catch (error) {
        onNotify((error as Error).message, 'error')
      }
    }
  }, [markDirty, accountId, addInline, editor, onNotify])

  // An image goes in the body, anything else in the tray. Plain text has no body to put one in,
  // so there everything is an attachment.
  const routeFiles = useCallback((files: File[]) => {
    if (text !== null) { addFiles(files); return }
    const images = files.filter(file => file.type.startsWith('image/'))
    const rest = files.filter(file => !file.type.startsWith('image/'))
    if (rest.length > 0) addFiles(rest)
    if (images.length > 0) void insertImages(images)
  }, [text, addFiles, insertImages])
```

Add the body-zone drag state beside `dragDepth` (after line 155):

```tsx
  const [overBody, setOverBody] = useState(false)
  const bodyDepth = useRef(0)
```

Add a shared reset and the body handlers after `onDrop` (line 181):

```tsx
  function resetDrag() {
    dragDepth.current = 0
    bodyDepth.current = 0
    setDropTarget(false)
    setOverBody(false)
  }
  function onBodyDragEnter(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    bodyDepth.current += 1
    setOverBody(true)
  }
  function onBodyDragLeave(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    bodyDepth.current = Math.max(0, bodyDepth.current - 1)
    if (bodyDepth.current === 0) setOverBody(false)
  }
  // Stops here: the surface handler below would attach what the body has just taken. It therefore
  // owes the surface its own reset, which is what resetDrag is for.
  function onBodyDrop(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    event.preventDefault()
    event.stopPropagation()
    resetDrag()
    const files = Array.from(event.dataTransfer.files)
    if (files.length > 0) routeFiles(files)
  }
  function onBodyPaste(event: React.ClipboardEvent) {
    const files = Array.from(event.clipboardData.files)
    if (files.length === 0) return
    // Capture phase: Squire's own paste handling runs on the way up and would see a half-taken event.
    event.preventDefault()
    routeFiles(files)
  }
```

and make the existing `onDrop` (line 174) use the shared reset, replacing its `dragDepth.current = 0` / `setDropTarget(false)` pair with:

```tsx
    resetDrag()
```

Replace the plain-text switch's adoption call (line 230):

```tsx
    attachments.adoptInline([...seedInline, ...insertedInline])
```

Wrap the editor in the render (lines 394-399):

```tsx
      <div className="compose-body" onDragEnter={onBodyDragEnter} onDragLeave={onBodyDragLeave}
        onDrop={onBodyDrop} onPasteCapture={onBodyPaste}>
        {text === null ? (
          <SquireEditor ref={setEditor} initialHtml={editorHtml} onChange={touchBody} onFormatChange={setActive} />
        ) : (
          <textarea className="compose-editor" data-testid="compose-text-editor" aria-label="Message body"
            value={text} onChange={e => { setText(e.target.value); touchBody() }} />
        )}
      </div>
```

and give the overlay its two labels (lines 453-455):

```tsx
      {dropTarget && (
        <div className="compose-drop-overlay">
          {overBody ? 'Drop image into the message' : 'Drop files to attach'}
        </div>
      )}
```

- [ ] **Step 9: Implement the CSS**

In `mail.css`, add before `.compose-editor` (line 1318):

```css
/* The editor's band, and the zone that tells an image dropped into the message from a file
   dropped on the composer. It has to carry the band-stack rules itself — the column gives its
   free height to one child, and that is now this wrapper rather than the editor inside it.
   position: relative anchors the image size bar. */
.compose-body {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  position: relative;
}
```

and add one line to `.compose-drop-overlay` (inside the block at line 1472):

```css
  pointer-events: none;
```

- [ ] **Step 10: Run the composer suite**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose`
Expected: PASS, including the pre-existing drag/drop and plain-text tests.

- [ ] **Step 11: Check it in a browser**

jsdom has no hit testing, so every test above passes whether or not the overlay covers its own target. Run the app, open the composer and confirm all four by eye:

1. Drag an image over the message body — the overlay reads "Drop image into the message"; drop it, and it appears in the body, not in the tray.
2. Drag the same file over the subject row — the overlay reads "Drop files to attach"; drop it, and it lands in the tray.
3. Drag a file in and out again without dropping — the overlay goes away.
4. Paste a screenshot into the body — it appears at the caret.

Then confirm the editor still fills its column and scrolls inside it rather than scrolling the whole composer: that is what the `.compose-body` rules in Step 9 are protecting, and a missing `min-height: 0` shows up here and nowhere else.

- [ ] **Step 12: Lint, typecheck, commit**

```bash
cd src/frontend && npm run lint && npm run typecheck
```

```bash
git add src/frontend/src/api.js src/frontend/src/modules/mail/compose/useStagedAttachments.ts src/frontend/src/modules/mail/compose/useStagedAttachments.test.tsx src/frontend/src/modules/mail/compose/ComposeView.tsx src/frontend/src/modules/mail/compose/ComposeView.test.tsx src/frontend/src/styles/mail.css
git commit -F - <<'EOF'
Paste or drop an image into the message body

The body is its own drop zone; an image staged there rides the send as a cid part
and is released with the composer, where any other file still goes to the tray.
EOF
```

---

### Task 4: Three display widths

Clicking an image in the body opens a small bar over it offering Small, Best fit and Original. Each writes a `width` attribute beside the `max-width: 100%` Task 2 already put there, so the choice lives in the markup and survives a draft save and reopen with no state to carry.

**Known limitation, deliberate:** the resize is not on Squire's undo stack — Ctrl+Z after a resize undoes the edit before it. Deleting the image and undoing *that* both work, since they are Squire's own operations. Do not add a feature-detected call to Squire's undo internals to paper over this.

**Files:**
- Create: `src/frontend/src/modules/mail/compose/ImageSizeBar.tsx`
- Create: `src/frontend/src/modules/mail/compose/ImageSizeBar.test.tsx`
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx` (the `.compose-body` div from Task 3)
- Modify: `src/frontend/src/styles/mail.css`
- Test: `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`

**Interfaces:**
- Consumes: the `.compose-body` wrapper and its `position: relative` (Task 3).
- Produces: `applyImageWidth(image: HTMLImageElement, width: number | null): void`, exported from `ImageSizeBar.tsx` so the test can claim what the buttons write without going through a layout.

- [ ] **Step 1: Write the failing component test**

Create `ImageSizeBar.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import ImageSizeBar, { applyImageWidth } from './ImageSizeBar'

function anImage() {
  const image = document.createElement('img')
  image.src = 'https://api.test.example/api/Mail/Attachments/i1/content'
  document.body.appendChild(image)
  return image
}

describe('applyImageWidth', () => {
  it('writes the width and keeps the message-width bound', () => {
    const image = anImage()

    applyImageWidth(image, 320)

    expect(image.getAttribute('width')).toBe('320')
    expect(image.style.maxWidth).toBe('100%')
  })

  // Original is the absence of a width, not a large one: an attribute naming a size the image
  // does not have would upscale a small one.
  it('drops the width for the original size', () => {
    const image = anImage()
    applyImageWidth(image, 640)

    applyImageWidth(image, null)

    expect(image.hasAttribute('width')).toBe(false)
    expect(image.style.maxWidth).toBe('100%')
  })
})

describe('ImageSizeBar', () => {
  it('applies the width its button names and reports the change', () => {
    const image = anImage()
    const onApplied = vi.fn()
    render(<ImageSizeBar image={image} onApplied={onApplied} />)

    fireEvent.click(screen.getByRole('button', { name: 'Small' }))

    expect(image.getAttribute('width')).toBe('320')
    expect(onApplied).toHaveBeenCalled()
  })

  it('marks the width the image already carries', () => {
    const image = anImage()
    applyImageWidth(image, 640)
    render(<ImageSizeBar image={image} onApplied={() => {}} />)

    expect(screen.getByRole('button', { name: 'Best fit' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'Small' })).toHaveAttribute('aria-pressed', 'false')
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/ImageSizeBar.test.tsx`
Expected: FAIL — cannot resolve `./ImageSizeBar`.

- [ ] **Step 3: Write the component**

Create `ImageSizeBar.tsx`:

```tsx
const WIDTHS: { label: string; value: number | null }[] = [
  { label: 'Small', value: 320 },
  { label: 'Best fit', value: 640 },
  { label: 'Original', value: null },
]

/**
 * The choice is written into the markup rather than held in React: that is what carries it
 * through a draft save and reopen, since the outgoing sanitiser keeps both width and style.
 * The bytes are untouched either way — only the rendered size changes.
 */
export function applyImageWidth(image: HTMLImageElement, width: number | null) {
  if (width === null) image.removeAttribute('width')
  else image.setAttribute('width', String(width))
  image.style.maxWidth = '100%'
}

interface Props {
  image: HTMLImageElement
  onApplied: () => void
}

export default function ImageSizeBar({ image, onApplied }: Props) {
  const current = image.getAttribute('width')
  return (
    <div className="compose-image-bar" role="group" aria-label="Image size">
      {WIDTHS.map(({ label, value }) => (
        <button key={label} type="button" aria-label={label}
          aria-pressed={current === (value === null ? null : String(value))}
          onClick={() => { applyImageWidth(image, value); onApplied() }}>
          {label}
        </button>
      ))}
    </div>
  )
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/ImageSizeBar.test.tsx`
Expected: PASS, 4 tests.

- [ ] **Step 5: Write the failing wiring test**

Append inside the `describe('inline images')` block in `ComposeView.test.tsx`:

```tsx
  it('opens the size bar on a click on an image in the body and closes it elsewhere', async () => {
    renderCompose()
    const body = screen.getByTestId('compose-editor')
    const image = document.createElement('img')
    image.src = 'https://api.test.example/api/Mail/Attachments/i5/content'
    body.appendChild(image)

    fireEvent.click(image)
    expect(await screen.findByRole('group', { name: 'Image size' })).toBeInTheDocument()

    fireEvent.click(body)
    await waitFor(() =>
      expect(screen.queryByRole('group', { name: 'Image size' })).not.toBeInTheDocument())
  })
```

- [ ] **Step 6: Run it to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/ComposeView.test.tsx -t "size bar"`
Expected: FAIL — no `Image size` group in the document.

- [ ] **Step 7: Wire it into ComposeView**

Add the import beside the others:

```tsx
import ImageSizeBar from './ImageSizeBar'
```

Add the state beside `overBody`:

```tsx
  const [sizingImage, setSizingImage] = useState<HTMLImageElement | null>(null)
```

Add the click handler beside the body drag handlers:

```tsx
  // A click on an image selects it for sizing; a click anywhere else in the body puts the bar
  // away, which is also what a click that lands on the caret means.
  function onBodyClick(event: React.MouseEvent) {
    const target = event.target as HTMLElement
    setSizingImage(target instanceof HTMLImageElement ? target : null)
  }
```

Drop the selection whenever the mode changes, next to the other effects:

```tsx
  useEffect(() => { setSizingImage(null) }, [text])
```

and drop it on any edit to the body, by extending `touchBody` (line 226) — typing after clicking an image would otherwise leave the bar hanging over a caret that has moved on:

```tsx
  const touchBody = useCallback(() => { markDirty(); setBodyTouched(true); setSizingImage(null) }, [markDirty])
```

Extend the `.compose-body` element from Task 3 with the handler and the bar:

```tsx
      <div className="compose-body" onDragEnter={onBodyDragEnter} onDragLeave={onBodyDragLeave}
        onDrop={onBodyDrop} onPasteCapture={onBodyPaste} onClick={onBodyClick}>
        {text === null ? (
          <SquireEditor ref={setEditor} initialHtml={editorHtml} onChange={touchBody} onFormatChange={setActive} />
        ) : (
          <textarea className="compose-editor" data-testid="compose-text-editor" aria-label="Message body"
            value={text} onChange={e => { setText(e.target.value); touchBody() }} />
        )}
        {sizingImage && (
          <ImageSizeBar image={sizingImage} onApplied={() => { touchBody(); setSizingImage(null) }} />
        )}
      </div>
```

- [ ] **Step 8: Style it**

Add to `mail.css`, after the `.compose-body` block from Task 3:

```css
/* Pinned to the top of the body rather than floated over the image: the editor scrolls under it,
   and a bar following an image out of view is a bar hanging outside a column that is
   overflow: hidden. */
.compose-image-bar {
  position: absolute;
  top: 8px;
  right: 12px;
  z-index: 5;
  display: flex;
  gap: 2px;
  padding: 3px;
  border: 1px solid var(--border);
  border-radius: 7px;
  background: var(--surface);
  box-shadow: 0 2px 8px rgb(0 0 0 / 18%);
}
.compose-image-bar button {
  padding: 4px 10px;
  border: none;
  border-radius: 5px;
  background: none;
  color: var(--text);
  font-size: 12.5px;
  cursor: pointer;
}
.compose-image-bar button:hover { background: var(--surface-sunken); }
.compose-image-bar button[aria-pressed='true'] {
  background: var(--action-primary);
  color: var(--action-primary-fg);
}
.compose-image-bar button[aria-pressed='true']:hover { background: var(--action-primary-hover); }
```

Every token here is one the toolbar beside it already uses (`mail.css:1388-1423`): `--surface-raised`, `--border`, `--radius-md`, `--surface-sunken`, `--action-primary`, `--action-primary-fg`, `--action-primary-hover`. **Never introduce a colour literal** — a token names a role.

- [ ] **Step 9: Run the composer suite**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose`
Expected: PASS.

- [ ] **Step 10: Check it in a browser**

Paste an image into the composer, then:

1. Click it — the bar appears, inside the column, not clipped by it.
2. Small, Best fit, Original each change the rendered width, and Original returns it to the size it came in at.
3. Scroll the editor with the bar open — it stays where it is, and never escapes the column.
4. Save the draft, leave the composer, reopen the draft — the width chosen is still applied.
5. Send it to yourself and read it — the image is in the body at the chosen width, and the attachment row is empty.

- [ ] **Step 11: Lint, typecheck, full suite, commit**

```bash
cd src/frontend && npm run lint && npm run typecheck && npm run test
```

```bash
git add src/frontend/src/modules/mail/compose/ImageSizeBar.tsx src/frontend/src/modules/mail/compose/ImageSizeBar.test.tsx src/frontend/src/modules/mail/compose/ComposeView.tsx src/frontend/src/modules/mail/compose/ComposeView.test.tsx src/frontend/src/styles/mail.css
git commit -F - <<'EOF'
Size an inline image from a bar over the body

Small, best fit or original, written as a width attribute so the choice survives
a draft round trip; the bytes always leave whole.
EOF
```

---

## Documentation

- [ ] **Update the two CLAUDE.md files**

`src/snoopy.microservice/CLAUDE.md` — in the `MailController` bullet, extend the `POST /api/Mail/Attachments` description: it now takes an `inline` flag that assigns a Content-ID and refuses a non-image with 400.

`src/frontend/CLAUDE.md` — in the Project paragraph, add that an image pasted or dropped on the message body becomes an inline part rather than an attachment, sized by a three-width bar, and that `.compose-body` is the drop zone that tells the two apart. Note the overlay's `pointer-events: none` as load-bearing: without it the overlay covers the very target it announces.

```bash
git add src/snoopy.microservice/CLAUDE.md src/frontend/CLAUDE.md
git commit -F - <<'EOF'
Document inline image composing

The staging flag on the API side, the body drop zone and its overlay rule on the
frontend side.
EOF
```
