/** One contact as `GET /api/Contacts` answers it. The API sends neither the vCard UID nor the raw
    card: no screen reads either. */
export interface Contact {
  id: string
  firstName: string | null
  lastName: string | null
  nickname: string | null
  isFavorite: boolean
  /** Ordered; `[0]` is the primary address. There is no separate flag to keep in step with it. */
  addresses: string[]
}

export interface ContactListResponse {
  contacts: Contact[]
}

/** What the editor submits. Same shape as `Contact` minus its id: the API assigns that. */
export interface ContactDraft {
  firstName: string | null
  lastName: string | null
  nickname: string | null
  isFavorite: boolean
  addresses: string[]
}
