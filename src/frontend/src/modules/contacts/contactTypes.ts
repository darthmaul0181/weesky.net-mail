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

/** One line of a family, as `GET /api/Contacts/{id}` answers it. Ordered for display by the
    server — `pref` then `position` — so no screen re-sorts what the card already ranked. */
export interface ContactDetailEmail {
  position: number
  address: string
  type: string
  pref: number
  params: string
  groupName: string
}

export interface ContactDetailPhone extends Omit<ContactDetailEmail, 'address'> {
  number: string
}

export interface ContactDetailPostal extends Omit<ContactDetailEmail, 'address'> {
  poBox: string | null
  extended: string | null
  street: string | null
  locality: string | null
  region: string | null
  postalCode: string | null
  country: string | null
}

/** The whole card, which `GET /api/Contacts` does not carry: the list holds what a tile needs,
    and dragging every contact's phones and notes through it would pay for the book to show a
    column. Fetched per contact, by the card alone. */
export interface ContactDetail {
  id: string
  firstName: string | null
  lastName: string | null
  nickname: string | null
  displayName: string | null
  middleName: string | null
  namePrefix: string | null
  nameSuffix: string | null
  organization: string | null
  department: string | null
  jobTitle: string | null
  birthday: string | null
  website: string | null
  notes: string | null
  isFavorite: boolean
  /** Whether `GET /api/Contacts/{id}/Photo` answers a picture. */
  hasPhoto: boolean
  addresses: ContactDetailEmail[]
  phones: ContactDetailPhone[]
  postalAddresses: ContactDetailPostal[]
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
