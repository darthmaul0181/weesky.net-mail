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
  /** Only the capture path sets this. The editor omits it and the API files the contact as
      "manual". */
  source?: 'captured'
}

export interface ContactImportError {
  /** The line in the file, header included — what the user reads in their spreadsheet. */
  line: number
  reason: string
}

/** The four counters count rows and add up to the file's data rows; `totalErrors` counts every
    reason, including those past the server's cap on `errors`. */
export interface ContactImportReport {
  created: number
  merged: number
  skipped: number
  failed: number
  totalErrors: number
  errors: ContactImportError[]
}
