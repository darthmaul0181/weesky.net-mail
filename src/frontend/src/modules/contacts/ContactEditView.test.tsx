import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactEditView from './ContactEditView'
import type {
  ContactDetail, ContactDetailEmail, ContactDraft, ContactDraftEmail,
} from './contactTypes'

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

// Augments bruno with the two repeatable families this task adds. 'OTHER' is not in PHONE_TYPES:
// it stands for a token the card carries and the table does not name (decision 4).
const withLines: ContactDetail = {
  ...bruno,
  phones: [
    { position: 0, number: '+32 493 82 44 15', type: 'CELL', pref: 101, params: '', groupName: '' },
    { position: 1, number: '+32 493 82 44 15', type: 'OTHER', pref: 101, params: '', groupName: '' },
  ],
  postalAddresses: [{
    position: 0, type: 'HOME,POSTAL', pref: 101, params: '', groupName: '',
    poBox: null, extended: null, street: 'Rue du Village 138',
    locality: 'Flémalle', region: 'Belgique', postalCode: '4400', country: 'Belgique',
  }],
}

// A vCard 3.0 round trip on a preferred email projects `PREF` into the very field the dropdown
// reads (défaut 4(a)); a quoted TYPE unquotes into a token the write-side grammar refuses (4(b)).
const messyTypes: ContactDetail = {
  ...bruno,
  phones: [
    { position: 0, number: '+32 493 82 44 15', type: 'INTERNET,PREF,WORK', pref: 101, params: '', groupName: '' },
  ],
  postalAddresses: [{
    position: 0, type: 'Work Email', pref: 101, params: '', groupName: '',
    poBox: null, extended: null, street: 'Rue Haute 1', locality: 'Liège',
    region: null, postalCode: '4000', country: 'Belgique',
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

  // Replaces the three "move up" cases: the button no longer displaces anything (decision 5), so
  // what has to be proved is that it writes pref and leaves the list where it stands.
  it('sends pref when a line is made the primary, and does not reorder the list', async () => {
    const { onSave } = setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /make this the primary/i }))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    const sent = onSave.mock.calls[0][0].addresses
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

    const sent = onSave.mock.calls[0][0].addresses
    expect(sent.map((a: ContactDraftEmail) => a.address)).toEqual(['a@x.be', 'b@x.be', 'c@x.be'])
    expect(sent.map((a: ContactDraftEmail) => a.pref)).toEqual([101, 101, 1])
    expect(within(screen.getByTestId('address-row-2')).getByText(/^primary$/i)).toBeInTheDocument()
  })

  // Le badge se calcule sur les mêmes lignes que l'enregistrement retient : vider le texte de la
  // ligne désignée laissait le badge sur une ligne vide pendant que la soumission promouvait
  // silencieusement la première ligne gardée.
  it('rend le badge à la première ligne gardée quand la ligne désignée est vidée', async () => {
    const { onSave } = setup({ contact: trio })
    await userEvent.click(within(screen.getByTestId('address-row-2'))
      .getByRole('button', { name: /make this the primary/i }))
    await userEvent.clear(screen.getByLabelText(/address 3/i))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(within(screen.getByTestId('address-row-0')).getByText(/^primary$/i)).toBeInTheDocument()
    expect(within(screen.getByTestId('address-row-2')).queryByText(/^primary$/i)).not.toBeInTheDocument()
    expect(onSave.mock.calls[0][0].addresses.map((a: ContactDraftEmail) => a.pref)).toEqual([1, 101])
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

    expect(onSave.mock.calls[0][0].addresses).toEqual([
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

    expect(onSave.mock.calls[0][0].displayName).toBe('Dr. Bruno Mertens')
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

    expect(onSave.mock.calls[0][0].phones).toEqual([
      { position: 0, number: '+32 493 82 44 15', type: 'CELL' },
      { position: 1, number: '+32 493 82 44 15', type: 'OTHER' },
    ])
    // The postal line in the very same save: its position, type and seven components must
    // survive a save that never touched it, exactly like the phone lines above.
    expect(onSave.mock.calls[0][0].postalAddresses).toEqual([
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
    await userEvent.click(bin[1])
    await userEvent.click(screen.getAllByRole('button', { name: /remove phone/i })[0])
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0][0].phones).toEqual([])
  })

  it("une adresse postale sans aucune composante n'est pas envoyée, type ou pas", async () => {
    const { onSave } = setup({ contact: bruno })
    await userEvent.click(screen.getByRole('button', { name: /add a postal address/i }))
    await userEvent.selectOptions(screen.getByLabelText(/postal address 1 type/i), 'WORK')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0][0].postalAddresses).toEqual([])
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

  // `ContactValidator.MaxAddressesPerContact` : la 51e ligne fait échouer l'enregistrement, le
  // aller-retour que la décision 8 existe pour éviter.
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
        position: i, type: 'HOME', pref: 101, params: '', groupName: '',
        poBox: null, extended: null, street: `Rue ${i}`, locality: null,
        region: null, postalCode: null, country: null,
      })),
    }
    setup({ contact: many })

    expect(screen.queryByRole('button', { name: /add a postal address/i })).not.toBeInTheDocument()
  })

  // Défaut 4(a) : un aller-retour 3.0 projette PREF dans le champ type lui-même
  // (`INTERNET,PREF,WORK`) ; le menu ne doit jamais l'offrir comme choix.
  it('strips PREF from a projected type before it ever reaches the phone dropdown', () => {
    setup({ contact: messyTypes })

    const select = screen.getByLabelText(/phone 1 type/i) as HTMLSelectElement
    expect(select.value).toBe('INTERNET,WORK')
    const optionTexts = Array.from(select.options).map(option => option.value)
    expect(optionTexts.some(value => value.toUpperCase().includes('PREF'))).toBe(false)
  })

  // Défaut 4(b) : un TYPE cité (`TYPE="Work Email"`) s'affiche brut dans le menu — décision 4 —
  // mais un jeton hors grammaire ne doit jamais repartir dans la requête, ou la fiche devient
  // impossible à enregistrer une seconde fois.
  it('shows a quoted type raw in the dropdown but drops it from what is submitted', async () => {
    const { onSave } = setup({ contact: messyTypes })

    expect(screen.getByLabelText(/postal address 1 type/i)).toHaveValue('Work Email')

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0][0].postalAddresses).toEqual([expect.objectContaining({ type: '' })])
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

    expect(onSave.mock.calls[0][0].jobTitle).toBe('Ingénieure')
  })

  // Sur ces champs le serveur lit `null` comme « la requête ne nomme pas le champ » : envoyer
  // null ici rendrait la société que l'utilisateur vient d'effacer.
  it('envoie une chaîne vide pour une société amorcée que l’utilisateur vide', async () => {
    const { onSave } = setup({ contact: { ...bruno, organization: 'Weesky' } })
    await userEvent.clear(screen.getByLabelText(/organisation/i))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0][0].organization).toBe('')
  })

  // L'autre moitié de la même convention : un champ intact n'est pas renvoyé du tout, ce qui
  // empêche une édition sans rapport de réécrire une valeur que le projecteur avait tronquée.
  it('envoie null pour une société amorcée que l’utilisateur ne touche pas', async () => {
    const { onSave } = setup({ contact: { ...bruno, organization: 'Weesky' } })
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0][0].organization).toBeNull()
  })

  it('un champ vidé reste affiché tant que le formulaire vit', async () => {
    setup({ contact: { ...bruno, organization: 'Weesky' } })
    await userEvent.clear(screen.getByLabelText(/organisation/i))

    expect(screen.getByLabelText(/organisation/i)).toBeInTheDocument()
  })

  it('l’anniversaire accepte une forme que nul calendrier n’exprime', async () => {
    const { onSave } = setup({ contact: bruno })
    await userEvent.type(screen.getByLabelText(/birthday/i), '--10-27')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(onSave.mock.calls[0][0].birthday).toBe('--10-27')
  })
})
