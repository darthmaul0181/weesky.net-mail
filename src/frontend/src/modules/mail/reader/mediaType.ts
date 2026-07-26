/**
 * A MIME type is case-insensitive (RFC 2045 §5.1), and servers do report `IMAGE/jpeg`: what the
 * reader shows as an image and what it inlines for a cid must agree on that, or a body references
 * a part the fetcher decided was not an image.
 */
export const isImageType = (contentType: string | null | undefined): boolean =>
  contentType?.toLowerCase().startsWith('image/') ?? false
