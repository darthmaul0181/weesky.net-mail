// MIME types are case-insensitive and servers do send `IMAGE/jpeg`: the viewer and the cid inliner
// must agree, or a body references a part the fetcher did not treat as an image.
export const isImageType = (contentType: string | null | undefined): boolean =>
  contentType?.toLowerCase().startsWith('image/') ?? false

/** The part an invitation was read from. The name is consulted too: a mailer that labels its
    `.ics` `application/octet-stream` is common, and the card would then double the chip. */
export const isCalendarType = (contentType: string, fileName = ''): boolean =>
  /^(text\/calendar|application\/ics)$/i.test(contentType) || /\.ics$/i.test(fileName)
