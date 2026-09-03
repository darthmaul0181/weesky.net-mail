/** One contact as `GET /api/Contacts` answers it. The API sends neither the vCard UID nor the raw
    card: no screen reads either. */
export interface Contact {
  id: string
  firstName: string | null
  lastName: string | null
  nickname: string | null
  /** The card's FN, and only when it says something the components do not: `VCardProjector`
      drops one it could have derived from them, so this is the name the user chose rather than
      the one a writer computed. Optional like `ContactDetail`'s fields and for the same reason —
      the API omits a null. */
  displayName?: string
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
    column. Fetched per contact, by the card alone. The API serialises with `WhenWritingNull`, so
    an absent value is omitted from the JSON rather than sent as `null` — every optional field is
    therefore `?:`, never `| null`. */
export interface ContactDetail {
  id: string
  firstName?: string
  lastName?: string
  nickname?: string
  displayName?: string
  middleName?: string
  namePrefix?: string
  nameSuffix?: string
  organization?: string
  department?: string
  jobTitle?: string
  birthday?: string
  website?: string
  notes?: string
  isFavorite: boolean
  /** Whether `GET /api/Contacts/{id}/Photo` answers a picture. */
  hasPhoto: boolean
  addresses: ContactDetailEmail[]
  phones: ContactDetailPhone[]
  postalAddresses: ContactDetailPostal[]
  /** The card's hash as it was read. Sent back on save so a stale write is refused rather than
      silently overwriting what another device stored meanwhile. Absent on a card the backfill
      never reached. */
  cardHash?: string
}

export interface ContactListResponse {
  contacts: Contact[]
}

/** One e-mail line on its way back to the card. Not a `ContactDetailEmail`: the draft carries
    neither `params` nor `groupName`, which no screen shows and the server preserves on its own. */
export interface ContactDraftEmail {
  /** The card rank this line replaces; null for a line the user just added. */
  position: number | null
  address: string
  type: string
  /** 1 on the primary, 101 to clear it, null to leave the card's own alone. */
  pref: number | null
}

export interface ContactDraftPhone {
  position: number | null
  number: string
  type: string
}

export interface ContactDraftPostal {
  position: number | null
  type: string
  poBox: string | null
  extended: string | null
  street: string | null
  locality: string | null
  region: string | null
  postalCode: string | null
  country: string | null
}

/** What the editor submits: the whole card, minus the id the API assigns. A scalar `null` clears
    the field the editor owns and leaves the card's own on every other — the convention
    `ContactWrite` documents on the server. */
export interface ContactDraft {
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
  /** `null` = unchanged, `''` = removed, base64 = replaced — the convention of the other optional
      scalars. Never an object URL: the preview is the editor's business, not the wire's. */
  photo: string | null
  isFavorite: boolean
  addresses: ContactDraftEmail[]
  /** Omitted by every producer but the editor: absent means the card keeps its own. */
  phones?: ContactDraftPhone[]
  postalAddresses?: ContactDraftPostal[]
  /** Only the capture path sets this. The editor omits it and the API files the contact as
      "manual". */
  source?: 'captured'
  /** The `ContactDetail.cardHash` the open form was seeded with. Omitted — never set to
      `undefined` — when the card carried none, which the API reads as "no version claimed". */
  cardHash?: string
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
