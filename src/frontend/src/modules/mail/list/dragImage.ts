import i18next from 'i18next'

/** The envelope a message drag carries. A bulleted list is the contacts book's, neutral over every
    drop target it offers — favourites and a group alike — unlike a star, which one of them is. */
export const ENVELOPE_GLYPH =
  '<rect x="2.5" y="4.5" width="19" height="15" rx="2.5"></rect><path d="m3 6 9 6.5L21 6"></path>'
export const LIST_GLYPH =
  '<path d="M9 6h11M9 12h11M9 18h11"></path><path d="M4 6h.01M4 12h.01M4 18h.01"></path>'

/**
 * The pill that follows the cursor during a drag: a glyph, what the drop will do, and how many
 * rows are riding along. Built as a detached node the caller hands to setDragImage — the browser
 * snapshots it, so it only has to exist at the moment of the drag, not stay in the visible tree.
 *
 * The label and the glyph are the caller's because the two modules drag different things: the mail
 * moves messages, the contacts book favourites or groups them.
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
