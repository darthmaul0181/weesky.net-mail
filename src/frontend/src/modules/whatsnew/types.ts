export type WhatsNewMark = 'shown' | 'listed' | 'seen'

/** `GET /api/WhatsNew`. The API omits empty stamps and an unwritten baseline. */
export interface WhatsNewItemState {
  id: string
  firstShownAt?: string
  listedAt?: string
  seenAt?: string
}

export interface WhatsNewState {
  baseline?: string
  items: WhatsNewItemState[]
}
