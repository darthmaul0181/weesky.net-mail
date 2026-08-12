import i18next from 'i18next'

/** The envelope a message drag carries, and the star a contact drag carries. */
export const ENVELOPE_GLYPH =
  '<rect x="2.5" y="4.5" width="19" height="15" rx="2.5"></rect><path d="m3 6 9 6.5L21 6"></path>'
export const STAR_GLYPH =
  '<polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon>'

/**
 * The pill that follows the cursor during a drag: a glyph, what the drop will do, and how many
 * rows are riding along. Built as a detached node the caller hands to setDragImage — the browser
 * snapshots it, so it only has to exist at the moment of the drag, not stay in the visible tree.
 *
 * The label and the glyph are the caller's because the two modules drag different things: the mail
 * moves messages, the contacts book stars them.
 */
export function buildDragPill(
  count: number,
  label: string = i18next.t('mail:move.actionMove'),
  glyph: string = ENVELOPE_GLYPH,
): HTMLElement {
  const pill = document.createElement('div')
  pill.className = 'drag-pill'

  const env = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
  env.setAttribute('class', 'drag-pill-env')
  env.setAttribute('width', '17')
  env.setAttribute('height', '17')
  env.setAttribute('viewBox', '0 0 24 24')
  env.setAttribute('fill', 'none')
  env.setAttribute('stroke', 'currentColor')
  env.setAttribute('stroke-width', '1.9')
  env.setAttribute('stroke-linecap', 'round')
  env.setAttribute('stroke-linejoin', 'round')
  env.innerHTML = glyph

  const labelNode = document.createElement('span')
  labelNode.textContent = label

  const badge = document.createElement('span')
  badge.className = 'drag-pill-count'
  badge.textContent = String(count)

  pill.append(env, labelNode, badge)
  return pill
}
