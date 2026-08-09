import i18next from 'i18next'

/**
 * The pill that follows the cursor during a drag: an envelope and the count moved. Built as a
 * detached node the caller hands to setDragImage — the browser snapshots it, so it only has to
 * exist at the moment of the drag, not stay in the visible tree.
 */
export function buildDragPill(count: number): HTMLElement {
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
  env.innerHTML = '<rect x="2.5" y="4.5" width="19" height="15" rx="2.5"></rect><path d="m3 6 9 6.5L21 6"></path>'

  const label = document.createElement('span')
  label.textContent = i18next.t('mail:move.actionMove')

  const badge = document.createElement('span')
  badge.className = 'drag-pill-count'
  badge.textContent = String(count)

  pill.append(env, label, badge)
  return pill
}
