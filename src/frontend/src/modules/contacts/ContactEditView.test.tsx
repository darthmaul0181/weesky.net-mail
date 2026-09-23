import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reducePhoto } from './contactPhoto'
import ContactEditView from './ContactEditView'
import type {
  ContactDetail, ContactDetailEmail, ContactDraft, ContactDraftEmail,
} from './contactTypes'

// The reducer is tested on its own: this file wants the editor, not the canvas.
vi.mock('./contactPhoto', () => ({
  PHOTO_UNREADABLE: 'editor.photoUnreadable',
  PHOTO_TOO_LARGE: 'editor.photoTooLarge',
  reducePhoto: vi.fn(async () => ({ base64: 'QUJD', blob: new Blob(['ABC']) })),
}))

beforeEach(() => {
  // jsdom has no object-URL API, and the preview uses it.
  URL.createObjectURL = vi.fn(() => 'blob:preview')
  URL.revokeObjectURL = vi.fn()
  vi.mocked(reducePhoto).mockResolvedValue({ base64: 'QUJD', blob: new Blob(['ABC']) })
})

/** The file input is `hidden` — the user clicks the avatar, never the input — and userEvent
    refuses to touch an invisible element, so the file is set directly. */
function choose(file: File) {
  fireEvent.change(screen.getByTestId('editor-photo-input'), { target: { files: [file] } })
}

function line(position: number, address: string): ContactDetailEmail {
  return { position, address, type: '', pref: 101, params: '', groupName: '' }
}

// Position 3 on the second line is not decorative: it proves the draft carries the card's own
// rank rather than recomputing one from the array index.
// The prefs are inverted on purpose, and no real card reads this way — `ContactStore` sorts the
// lines by (pref, position), so the preferred one always arrives first. That sort is exactly why
// the editor designates from the arrival order and seeds pref null, which `pref: [1, 101]` shows.
const bruno: ContactDetail = {
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  displayName: 'Dr. Bruno Mertens', isFavorite: false, hasPhoto: false,
  addresses: [
    { position: 0, address: 'bruno@x.be', type: 'INTERNET', pref: 101, params: '', groupName: 'item1' },
    { position: 3, address: 'b.mertens@wk.be', type: 'WORK', pref: 1, params: '', groupName: '' },
  ],
  phones: [], postalAddresses: [],
}

const solo: ContactDetail = {
  id: 's', isFavorite: false, hasPhoto: false,
  addresses: [line(0, 'solo@x.be')], phones: [], postalAddresses: [],
}

// Three rows are what tells "designate this one" apart from "promote to the top" or "reverse the
// list" — with only two rows (bruno, above) all three read the same.
const trio: ContactDetail = {
  id: 't', isFavorite: false, hasPhoto: false,
  addresses: [line(0, 'a@x.be'), line(1, 'b@x.be'), line(2, 'c@x.be')],
  phones: [], postalAddresses: [],
}

const addressless: ContactDetail = {
  id: 'z', firstName: 'Zoe', isFavorite: false, hasPhoto: false,
  addresses: [], phones: [], postalAddresses: [],
}

// Augments bruno with the two repeatable families. 'OTHER' is not in PHONE_TYPES: it stands for a
// token the card carries and the table does not name.
const withLines: ContactDetail = {
  ...bruno,
  phones: [
    { position: 0, number: '+32 493 82 44 15', type: 'CELL', pref: 101, params: '', groupName: '' },
    { position: 1, number: '+32 493 82 44 15', type: 'OTHER', pref: 101, params: '', groupName: '' },
  ],
  postalAddresses: [{
    position: 0, type: 'HOME,POSTAL', pref: 101, params: '', groupName: '',
    street: 'Rue du Village 138',
    locality: 'Flémalle', region: 'Belgique', postalCode: '4400', country: 'Belgique',
  }],
}

// A vCard 3.0 round trip on a preferred email projects `PREF` into the very field the dropdown
// reads (defect 4(a)); a quoted TYPE unquotes into a token the write-side grammar refuses (4(b)).
const messyTypes: ContactDetail = {
  ...bruno,
  phones: [
    { position: 0, number: '+32 493 82 44 15', type: 'INTERNET,PREF,WORK', pref: 101, params: '', groupName: '' },
  ],
  postalAddresses: [{
    position: 0, type: 'Work Email', pref: 101, params: '', groupName: '',
    street: 'Rue Haute 1', locality: 'Liège',
    postalCode: '4000', country: 'Belgique',
  }],
}

/** The ten fields the editor draws no box for yet: a create names none of them. */
const noCarriedFields = {
  displayName: null, middleName: null, namePrefix: null, nameSuffix: null, organization: null,
  department: null, jobTitle: null, birthday: null, website: null, notes: null,
}

/** `onSave` is not overridable: the returned spy has to be the one that was rendered, and it is
    typed so a draft read out of `mock.calls` is checked rather than `any`. */
function setup(overrides: Omit<Partial<Parameters<typeof ContactEditView>[0]>, 'onSave'> = {}) {
  const onSave = vi.fn<(draft: ContactDraft) => void>()
  const props = {
    contact: null as ContactDetail | null, saving: false, error: null as string | null,
    onCancel: vi.fn(), ...overrides,
  }
  render(<ContactEditView {...props} onSave={onSave} />)
  return { ...props, onSave }
}

describe('ContactEditView', () => {
  // Both halves, side by side in the document: one component serves the two modes, so the heading
  // is the only thing telling the user which one they are in.
  it('heads a create as New contact and an edit as Edit contact', () => {
    setup()
    setup({ contact: bruno })

    expect(screen.getByRole('heading', { name: /new contact/i })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /edit contact/i })).toBeInTheDocument()
  })

  it('seeds every field from the contact being edited', () => {
    setup({ contact: bruno })

    expect(screen.getByLabelText(/first name/i)).toHaveValue('Bruno')
    expect(screen.getByLabelText(/last name/i)).toHaveValue('Mertens')
    expect(screen.getByLabelText(/nickname/i)).toHaveValue('bru')
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('bruno@x.be')
    expect(screen.getByLabelText(/address 2/i)).toHaveValue('b.mertens@wk.be')
  })

  it('starts a create with one empty address row', () => {
    setup()

    expect(screen.getByLabelText(/address 1/i)).toHaveValue('')
    expect(screen.queryByLabelText(/address 2/i)).not.toBeInTheDocument()
  })

  // The server allows a contact with only a name, so an edited contact's `addresses` can arrive
  // empty too, not just a brand-new create — the same empty-row seed has to cover both.
  it('seeds one empty address row when the contact being edited has none at all', () => {
    setup({ contact: addressless })

    expect(screen.getByLabelText(/address 1/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('')
  })

  // Position 0 is the primary by definition: the badge is on the first row, and it moves when
  // the rows are reordered rather than being a flag of its own.
  it('badges the first address row as the primary', () => {
    setup({ contact: bruno })

    // The badge itself, not the row's text: the other row's button is named "Make this the
    // primary address" and would satisfy a substring match on the whole row.
    expect(within(screen.getByTestId('address-row-0')).getByText(/^primary$/i)).toBeInTheDocument()
    expect(within(screen.getByTestId('address-row-1')).queryByText(/^primary$/i))
      .not.toBeInTheDocument()
  })

  it('adds an address row on demand', async () => {
    setup()

    await userEvent.click(screen.getByRole('button', { name: /add an address/i }))

    expect(screen.getByLabelText(/address 2/i)).toBeInTheDocument()
  })

  it('removes an address row', async () => {
    setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /remove address 2/i }))

    expect(screen.queryByLabelText(/address 2/i)).not.toBeInTheDocument()
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('bruno@x.be')
  })

  // The floor the two-address removal test above cannot reach: with only one row left, removing
  // it must not empty the list, or a create-mode user is left with no box to type into at all.
  it('never drops to zero address rows: removing the last one leaves an empty row', async () => {
    setup({ contact: solo })

    await userEvent.click(screen.getByRole('button', { name: /remove address 1/i }))

    expect(screen.getByLabelText(/address 1/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('')
  })

  // Replaces the three "move up" cases: the button no longer displaces anything, so what has to
  // be proved is that it writes pref and leaves the list where it stands.
  it('sends pref when a line is made the primary, and does not reorder the list', async () => {
    const { onSave } = setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /make this the primary/i }))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    const sent = onSave.mock.calls[0]![0].addresses
    expect(sent.map((a: ContactDraftEmail) => a.address))
      .toEqual(['bruno@x.be', 'b.mertens@wk.be'])
    expect(sent.map((a: ContactDraftEmail) => a.pref)).toEqual([101, 1])
  })

  // With three rows, "designate this one" is told apart from "promote to the top" and from
  // "reverse the list" — all three read the same on the two-row fixture above.
  it('designates the third line and leaves the other two cleared', async () => {
    const { onSave } = setup({ contact: trio })

    await userEvent.click(within(screen.getByTestId('address-row-2'))
      .getByRole('button', { name: /make this the primary/i }))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    const sent = onSave.mock.calls[0]![0].addresses
    expect(sent.map((a: ContactDraftEmail) => a.address)).toEqual(['a@x.be', 'b@x.be', 'c@x.be'])
    expect(sent.map((a: ContactDraftEmail) => a.pref)).toEqual([101, 101, 1])
    expect(within(screen.getByTestId('address-row-2')).getByText(/^primary$/i)).toBeInTheDocument()
  })

  // The badge is computed off the same lines the save keeps: emptying the text of the designated
  // line left the badge on a blank line while the submission silently promoted the first line
  // still kept.
  it('rend le badge à la première ligne gardée quand la ligne désignée est vidée', async () => {
    const { onSave } = setup({ contact: trio })
    await userEvent.click(within(screen.getByTestId('address-row-2'))
      .getByRole('button', { name: /make this the primary/i }))
    await userEvent.clear(screen.getByLabelText(/address 3/i))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(within(screen.getByTestId('address-row-0')).getByText(/^primary$/i)).toBeInTheDocument()
    expect(within(screen.getByTestId('address-row-2')).queryByText(/^primary$/i)).not.toBeInTheDocument()
    expect(onSave.mock.calls[0]![0].addresses.map((a: ContactDraftEmail) => a.pref)).toEqual([1, 101])
  })

  it('offers no make-primary control on the row that already is the primary', () => {
    setup({ contact: bruno })

    expect(within(screen.getByTestId('address-row-0'))
      .queryByRole('button', { name: /make this the primary/i })).not.toBeInTheDocument()
  })

  // The card's rank, not the array index: without it the composer treats every line as new and
  // rebuilds the EMAIL block, losing its group prefix, its parameters and its X- parameters.
  it('returns the position of every seeded line, and null for a new one', async () => {
    const { onSave } = setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /add an address/i }))
    await userEvent.type(screen.getByLabelText(/address 3/i), 'troisieme@x.be')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].addresses).toEqual([
      { position: 0, address: 'bruno@x.be', type: 'INTERNET', pref: 1 },
      { position: 3, address: 'b.mertens@wk.be', type: 'WORK', pref: 101 },
      { position: null, address: 'troisieme@x.be', type: '', pref: 101 },
    ])
  })

  // Without it the server recomputes FN from the name parts, and FN:Dr. Bruno Mertens comes back
  // as FN:Bruno Mertens after an edit that never touched the name.
  it('returns the display name the card carries, untouched', async () => {
    const { onSave } = setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].displayName).toBe('Dr. Bruno Mertens')
  })

  // The gate the backend also enforces. Refusing here is what keeps the user from a round trip
  // whose only outcome is an error banner.
  it('keeps save disabled while neither a name nor an address is filled', () => {
    setup()

    expect(screen.getByRole('button', { name: /save contact/i })).toBeDisabled()
  })

  it('enables save on a name alone', async () => {
    setup()

    await userEvent.type(screen.getByLabelText(/first name/i), 'Bruno')

    expect(screen.getByRole('button', { name: /save contact/i })).toBeEnabled()
  })

  it('enables save on an address alone', async () => {
    setup()

    await userEvent.type(screen.getByLabelText(/address 1/i), 'bruno@x.be')

    expect(screen.getByRole('button', { name: /save contact/i })).toBeEnabled()
  })

  it('submits the draft, blank address rows dropped', async () => {
    const props = setup()
    await userEvent.type(screen.getByLabelText(/first name/i), 'Bruno')
    await userEvent.click(screen.getByRole('button', { name: /add an address/i }))
    await userEvent.type(screen.getByLabelText(/address 1/i), 'bruno@x.be')

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith({
      ...noCarriedFields,
      photo: null,
      firstName: 'Bruno', lastName: null, nickname: null, isFavorite: false,
      addresses: [{ position: null, address: 'bruno@x.be', type: '', pref: 1 }],
      phones: [], postalAddresses: [],
    })
  })

  it('sends null rather than an empty string for a blank name', async () => {
    const props = setup()
    await userEvent.type(screen.getByLabelText(/address 1/i), 'a@x.be')

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith(expect.objectContaining({ firstName: null, nickname: null }))
  })

  it('carries the favourite flag through', async () => {
    const props = setup({ contact: { ...bruno, isFavorite: true } })

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith(expect.objectContaining({ isFavorite: true }))
  })

  // The column widths, spelled out rather than read from the component: a bound that drifts from
  // VARCHAR(100)/VARCHAR(320) sends the write into a strict-mode MariaDB error, i.e. a 500.
  it('bounds every field to its column width', () => {
    setup({ contact: bruno })

    expect(screen.getByLabelText(/first name/i)).toHaveAttribute('maxlength', '100')
    expect(screen.getByLabelText(/last name/i)).toHaveAttribute('maxlength', '100')
    expect(screen.getByLabelText(/nickname/i)).toHaveAttribute('maxlength', '100')
    expect(screen.getByLabelText(/address 1/i)).toHaveAttribute('maxlength', '320')
    expect(screen.getByLabelText(/address 2/i)).toHaveAttribute('maxlength', '320')
  })

  it('surfaces a server error at the top of the form', () => {
    setup({ error: "'nope' is not a valid email address" })

    expect(screen.getByRole('alert')).toHaveTextContent('not a valid email address')
  })

  it('disables save and shows a spinner while saving', () => {
    setup({ contact: bruno, saving: true })

    expect(screen.getByRole('button', { name: /save contact/i })).toBeDisabled()
    expect(screen.getByTestId('editor-spinner')).toBeInTheDocument()
  })

  it('cancels through the ✕', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /close the editor/i }))

    expect(props.onCancel).toHaveBeenCalled()
  })

  it('un type absent de la liste survit à un enregistrement qui ne touche pas sa ligne', async () => {
    const { onSave } = setup({ contact: withLines })
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].phones).toEqual([
      { position: 0, number: '+32 493 82 44 15', type: 'CELL' },
      { position: 1, number: '+32 493 82 44 15', type: 'OTHER' },
    ])
    // The postal line in the very same save: its position, type and seven components must
    // survive a save that never touched it, exactly like the phone lines above.
    expect(onSave.mock.calls[0]![0].postalAddresses).toEqual([
      {
        position: 0, type: 'HOME,POSTAL', poBox: null, extended: null,
        street: 'Rue du Village 138', locality: 'Flémalle', region: 'Belgique',
        postalCode: '4400', country: 'Belgique',
      },
    ])
  })

  it('vider une famille envoie une liste vide, pas une omission', async () => {
    const { onSave } = setup({ contact: withLines })
    const bin = screen.getAllByRole('button', { name: /remove phone/i })
    await userEvent.click(bin[1]!)
    await userEvent.click(screen.getAllByRole('button', { name: /remove phone/i })[0]!)
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].phones).toEqual([])
  })

  it("une adresse postale sans aucune composante n'est pas envoyée, type ou pas", async () => {
    const { onSave } = setup({ contact: bruno })
    await userEvent.click(screen.getByRole('button', { name: /add a postal address/i }))
    await userEvent.selectOptions(screen.getByLabelText(/postal address 1 type/i), 'WORK')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].postalAddresses).toEqual([])
  })

  it('au plafond, le bouton d’ajout de la famille disparaît', async () => {
    const many = {
      ...bruno,
      phones: Array.from({ length: 10 }, (_, i) => (
        { position: i, number: `+3247000000${i}`, type: 'CELL', pref: 101, params: '', groupName: '' })),
    }
    setup({ contact: many })

    expect(screen.queryByRole('button', { name: /add a phone/i })).not.toBeInTheDocument()
  })

  // `ContactValidator.MaxAddressesPerContact`: the 51st line fails the save, the round trip this
  // guard exists to avoid.
  it('au plafond des adresses, le bouton d’ajout disparaît aussi', async () => {
    const many = {
      ...bruno,
      addresses: Array.from({ length: 50 }, (_, i) => line(i, `a${i}@x.be`)),
    }
    setup({ contact: many })

    expect(screen.queryByRole('button', { name: /add an address/i })).not.toBeInTheDocument()
  })

  it('au plafond des adresses postales, le bouton d’ajout disparaît aussi', async () => {
    const many = {
      ...bruno,
      postalAddresses: Array.from({ length: 10 }, (_, i) => ({
        position: i, type: 'HOME', pref: 101, params: '', groupName: '', street: `Rue ${i}`,
      })),
    }
    setup({ contact: many })

    expect(screen.queryByRole('button', { name: /add a postal address/i })).not.toBeInTheDocument()
  })

  // Defect 4(a): a 3.0 round trip projects PREF into the type field itself
  // (`INTERNET,PREF,WORK`); the menu must never offer it as a choice.
  it('strips PREF from a projected type before it ever reaches the phone dropdown', () => {
    setup({ contact: messyTypes })

    const select = screen.getByLabelText<HTMLSelectElement>(/phone 1 type/i)
    expect(select.value).toBe('INTERNET,WORK')
    const optionTexts = Array.from(select.options).map(option => option.value)
    expect(optionTexts.some(value => value.toUpperCase().includes('PREF'))).toBe(false)
  })

  // Defect 4(b): a quoted TYPE (`TYPE="Work Email"`) shows raw in the menu, but a token outside
  // the grammar must never go back out in the request, or the card becomes impossible to save a
  // second time.
  it('shows a quoted type raw in the dropdown but drops it from what is submitted', async () => {
    const { onSave } = setup({ contact: messyTypes })

    expect(screen.getByLabelText(/postal address 1 type/i)).toHaveValue('Work Email')

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].postalAddresses).toEqual([expect.objectContaining({ type: '' })])
  })

  /* The nickname follows the same rule as the other eight since it left the hero: one card in a
     hundred carries one, and its box used to take a whole line on all the others. */
  it('cache le surnom d’une carte qui n’en porte pas, et le propose au menu', async () => {
    setup({ contact: solo })

    expect(screen.queryByLabelText(/nickname/i)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /add a field/i }))
    expect(screen.getByRole('menuitem', { name: /nickname/i })).toBeInTheDocument()
  })

  it('n’affiche pas le surnom à la création', () => {
    setup()

    expect(screen.queryByLabelText(/nickname/i)).not.toBeInTheDocument()
  })

  it('affiche d’office le surnom d’une carte qui en porte un', () => {
    setup({ contact: bruno })

    expect(screen.getByLabelText(/nickname/i)).toHaveValue('bru')
  })

  it('un surnom ajouté depuis le menu part à l’enregistrement', async () => {
    const { onSave } = setup({ contact: solo })
    await userEvent.click(screen.getByRole('button', { name: /add a field/i }))
    await userEvent.click(screen.getByRole('menuitem', { name: /nickname/i }))
    await userEvent.type(screen.getByLabelText(/nickname/i), 'bru')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].nickname).toBe('bru')
  })

  /* The nickname and the display name are not fields like the other eight: `Apply` replaces a
     name, null included, so null there is the user emptying the box — never "the request does
     not name this field". An empty string would leave an empty NICKNAME on the card. */
  it('envoie null pour un surnom que l’utilisateur vide', async () => {
    const { onSave } = setup({ contact: bruno })
    await userEvent.clear(screen.getByLabelText(/nickname/i))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].nickname).toBeNull()
  })

  it('un contact qui n’a que son surnom reste enregistrable', async () => {
    const { onSave } = setup({ contact: { ...solo, addresses: [], nickname: 'bru' } })
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].nickname).toBe('bru')
  })

  it('affiche d’office un champ que la carte remplit, et ne le propose pas au menu', async () => {
    setup({ contact: { ...bruno, organization: 'Weesky' } })

    expect(screen.getByLabelText(/organisation/i)).toHaveValue('Weesky')
    await userEvent.click(screen.getByRole('button', { name: /add a field/i }))
    expect(screen.queryByRole('menuitem', { name: /organisation/i })).not.toBeInTheDocument()
  })

  it('un champ ajouté depuis le menu devient saisissable et part à l’enregistrement', async () => {
    const { onSave } = setup({ contact: bruno })
    await userEvent.click(screen.getByRole('button', { name: /add a field/i }))
    await userEvent.click(screen.getByRole('menuitem', { name: /job title/i }))
    await userEvent.type(screen.getByLabelText(/job title/i), 'Ingénieure')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].jobTitle).toBe('Ingénieure')
  })

  // On these fields the server reads `null` as "the request does not name the field": sending
  // null here would give back the organisation the user just cleared.
  it('envoie une chaîne vide pour une société amorcée que l’utilisateur vide', async () => {
    const { onSave } = setup({ contact: { ...bruno, organization: 'Weesky' } })
    await userEvent.clear(screen.getByLabelText(/organisation/i))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].organization).toBe('')
  })

  // The other half of the same convention: an untouched field is not sent at all, which stops an
  // unrelated edit from rewriting a value the projector had truncated.
  it('envoie null pour une société amorcée que l’utilisateur ne touche pas', async () => {
    const { onSave } = setup({ contact: { ...bruno, organization: 'Weesky' } })
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].organization).toBeNull()
  })

  it('un champ vidé reste affiché tant que le formulaire vit', async () => {
    setup({ contact: { ...bruno, organization: 'Weesky' } })
    await userEvent.clear(screen.getByLabelText(/organisation/i))

    expect(screen.getByLabelText(/organisation/i)).toBeInTheDocument()
  })

  it('l’anniversaire d’une carte de téléphone se lit comme une date', () => {
    setup({ contact: { ...bruno, birthday: '19930621T115900Z' } })

    expect(screen.getByLabelText(/birthday/i)).toHaveValue('21/06/1993')
  })

  /* What the field shows is no longer what the card carries, so the equality that decides whether
     to send anything is judged on the typed form. Without that, editing the name would rewrite
     the birthday and it would lose its time. */
  it('un anniversaire non touché n’est pas réécrit par une modification voisine', async () => {
    const { onSave } = setup({ contact: { ...bruno, birthday: '19930621T115900Z' } })
    await userEvent.type(screen.getByLabelText(/first name/i), 'x')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].birthday).toBeNull()
  })

  it('un anniversaire tapé part dans l’orthographe du vCard', async () => {
    const { onSave } = setup({ contact: bruno })
    await userEvent.type(screen.getByLabelText(/birthday/i), '27/10/1979')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].birthday).toBe('1979-10-27')
  })

  it('l’anniversaire accepte une forme que nul calendrier n’exprime', async () => {
    const { onSave } = setup({ contact: bruno })
    await userEvent.type(screen.getByLabelText(/birthday/i), '--10-27')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0]![0].birthday).toBe('--10-27')
  })

  // The banner: the photo the card carries, never a door to replace it.
  it('montre la photo du contact quand la mise en page en résout une', () => {
    setup({ contact: bruno, photo: 'blob:une-photo' })

    expect(screen.getByTestId('editor-photo')).toHaveAttribute('src', 'blob:une-photo')
    expect(screen.queryByTestId('editor-avatar-blank')).not.toBeInTheDocument()
  })

  // Without a photo the card shows its initials, not an anonymous disc.
  it('replie sur les initiales du contact quand la carte ne porte pas de photo', () => {
    setup({ contact: bruno })

    expect(screen.getByTestId('editor-initials')).toHaveTextContent('BM')
    expect(screen.queryByTestId('editor-avatar-blank')).not.toBeInTheDocument()
  })

  // Same box when creating, so the banner's height does not jump between the two modes.
  it('tient la place de la photo par une pastille en creation', () => {
    setup({ contact: null })

    expect(screen.getByTestId('editor-avatar-blank')).toBeInTheDocument()
    expect(screen.queryByTestId('editor-photo')).not.toBeInTheDocument()
  })

  // The star describes the contact, so it sits in the banner rather than among the fields, but it
  // keeps the accessible name its old labelled row gave it.
  it("porte l'etoile dans le bandeau, a cote des noms", () => {
    setup({ contact: bruno })

    const star = screen.getByLabelText(/favourite/i)
    expect(star).toHaveAttribute('aria-pressed', 'false')
    expect(star.closest('.contact-editor-hero')).not.toBeNull()
  })

  // ---- the photo ----------------------------------------------------------------------------

  it('opens the picker from the avatar itself', async () => {
    setup({ contact: bruno })
    const opened = vi.spyOn(screen.getByTestId('editor-photo-input'), 'click')

    await userEvent.click(screen.getByRole('button', { name: 'Change photo' }))

    expect(opened).toHaveBeenCalled()
  })

  it('submits the chosen photo as base64', async () => {
    const { onSave } = setup({ contact: bruno })

    choose(new File(['x'], 'p.jpg'))
    await screen.findByTestId('editor-photo')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: 'QUJD' }))
  })

  it('shows the chosen photo at once', async () => {
    setup({ contact: bruno })

    choose(new File(['x'], 'p.jpg'))

    expect(await screen.findByTestId('editor-photo')).toHaveAttribute('src', 'blob:preview')
  })

  it('submits an empty string when the seeded photo is removed', async () => {
    const { onSave } = setup({ contact: bruno, photo: 'blob:seeded' })

    await userEvent.click(screen.getByRole('button', { name: /remove photo/i }))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: '' }))
  })

  // Covers the photo arriving after mount, as the blob does in the real app. A seed frozen at
  // mount would read null here, and the removal would go out as null. `setup` only renders once,
  // so this test drives its own rerender.
  it('still removes a photo that arrived after mount', async () => {
    const onSave = vi.fn<(draft: ContactDraft) => void>()
    const props = { contact: bruno, saving: false, error: null, onCancel: vi.fn(), onSave }
    const { rerender } = render(<ContactEditView {...props} photo={null} />)

    rerender(<ContactEditView {...props} photo="blob:late" />)
    await userEvent.click(screen.getByRole('button', { name: /remove photo/i }))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: '' }))
  })

  it('returns to the seeded photo when a local choice is removed', async () => {
    const { onSave } = setup({ contact: bruno, photo: 'blob:seeded' })

    choose(new File(['x'], 'p.jpg'))
    await screen.findByTestId('editor-photo')
    await userEvent.click(screen.getByRole('button', { name: /remove photo/i }))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(screen.getByTestId('editor-photo')).toHaveAttribute('src', 'blob:seeded')
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: null }))
  })

  it('reports an unreadable file under the avatar, not in the banner', async () => {
    vi.mocked(reducePhoto).mockRejectedValueOnce(new Error('editor.photoUnreadable'))
    const { onSave } = setup({ contact: bruno })

    choose(new File(['x'], 'p.heic'))
    await screen.findByTestId('editor-photo-error')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(screen.getByTestId('editor-photo-error')).toHaveTextContent('This file cannot be read. Choose a JPEG, PNG, GIF or WebP image.')
    expect(screen.queryByRole('alert')).toBeNull()
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: null }))
  })
})
