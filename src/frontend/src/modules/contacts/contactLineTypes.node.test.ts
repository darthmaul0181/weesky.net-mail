import { describe, expect, it } from 'vitest'
import { PHONE_TYPES, sanitizeTypeForSubmit, stripPref, typeOptions } from './contactLineTypes'

describe('typeOptions', () => {
  it('offre la liste connue telle quelle', () => {
    expect(typeOptions(PHONE_TYPES, 'CELL')).toEqual([...PHONE_TYPES])
  })

  it('ajoute le type de la carte quand la liste ne le contient pas', () => {
    expect(typeOptions(PHONE_TYPES, 'OTHER')).toEqual([...PHONE_TYPES, 'OTHER'])
  })

  it('ignore la casse et les espaces avant de conclure à un inconnu', () => {
    expect(typeOptions(PHONE_TYPES, 'cell')).toEqual([...PHONE_TYPES])
  })

  it('une ligne neuve sans type ne fabrique pas une option vide', () => {
    expect(typeOptions(PHONE_TYPES, '')).toEqual([...PHONE_TYPES])
  })
})

describe('stripPref', () => {
  it('retire le jeton PREF projeté par un aller-retour 3.0', () => {
    expect(stripPref('INTERNET,PREF,WORK')).toBe('INTERNET,WORK')
  })

  it('ignore la casse du jeton', () => {
    expect(stripPref('internet,pref')).toBe('internet')
  })

  it('laisse un type sans PREF intact', () => {
    expect(stripPref('HOME,VOICE')).toBe('HOME,VOICE')
  })
})

describe('sanitizeTypeForSubmit', () => {
  it("retire un jeton porteur d'un caractère hors grammaire", () => {
    expect(sanitizeTypeForSubmit('Work Email')).toBe('')
  })

  it('garde les jetons valides et retire seulement celui qui ne l’est pas', () => {
    expect(sanitizeTypeForSubmit('HOME,Work Email')).toBe('HOME')
  })

  it('laisse un type déjà conforme intact', () => {
    expect(sanitizeTypeForSubmit('HOME,VOICE')).toBe('HOME,VOICE')
  })
})
