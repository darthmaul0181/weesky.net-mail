import { useRef, useState } from 'react'
import { RuleCard } from './RuleCard'
import type { SieveRuleWrite } from './rulesTypes'

type Persist = (rules: SieveRuleWrite[]) => Promise<void>

/** The drag's state, kept by the page so it outlives the list it is drawn in. */
export function useRuleDrag(rules: SieveRuleWrite[], persistRules: Persist) {
  // The dragged rule's id, never its index: a save from elsewhere can shrink or reorder `rules`
  // mid-drag, and an index would then move and save the wrong rule, or point past the end.
  const dragIdRef = useRef<string | null>(null)
  const [dropIndex, setDropIndex] = useState<number | null>(null)

  function handleDrop(index: number) {
    const id = dragIdRef.current
    dragIdRef.current = null
    setDropIndex(null)
    if (id === null) return
    const from = rules.findIndex(r => r.id === id)
    // Gone: the drag started on a rule a concurrent save has since removed. Nothing to move.
    if (from === -1 || from === index) return
    const u = [...rules]
    const [moved] = u.splice(from, 1)
    u.splice(index, 0, moved!)
    // The shrink between drag start and drop can also leave the order exactly as it was.
    // u is rules with one item spliced out and back in, so the two are always the same length.
    if (u.every((r, i) => r.id === rules[i]!.id)) return
    void persistRules(u)
  }

  function handleDragEnd() {
    dragIdRef.current = null
    setDropIndex(null)
  }

  return {
    dropIndex,
    start: (id: string) => { dragIdRef.current = id },
    over: (index: number, id: string) => { if (dragIdRef.current !== null && dragIdRef.current !== id) setDropIndex(index) },
    drop: handleDrop,
    end: handleDragEnd,
  }
}

interface RuleListProps {
  rules: SieveRuleWrite[]
  drag: ReturnType<typeof useRuleDrag>
  persistRules: Persist
  onEdit: (rule: SieveRuleWrite) => void
  onDelete: (rule: SieveRuleWrite) => void
}

export function RuleList({ rules, drag, persistRules, onEdit, onDelete }: RuleListProps) {
  function handleToggleEnabled(index: number, enabled: boolean) {
    void persistRules(rules.map((r, i) => i === index ? { ...r, enabled } : r))
  }

  function handleMoveUp(index: number) {
    if (index === 0) return
    const u = [...rules];
    // index is in [1, rules.length - 1] here, so both u[index - 1] and u[index] exist.
    [u[index - 1], u[index]] = [u[index]!, u[index - 1]!]
    void persistRules(u)
  }

  function handleMoveDown(index: number) {
    if (index === rules.length - 1) return
    const u = [...rules];
    // index is in [0, rules.length - 2] here, so both u[index] and u[index + 1] exist.
    [u[index], u[index + 1]] = [u[index + 1]!, u[index]!]
    void persistRules(u)
  }

  return (
    <div className="rules-list">
      {rules.map((rule, i) => (
        <RuleCard
          key={rule.id ?? i}
          rule={rule}
          isFirst={i === 0}
          isLast={i === rules.length - 1}
          onEdit={() => onEdit(rule)}
          onDelete={() => onDelete(rule)}
          onToggleEnabled={enabled => handleToggleEnabled(i, enabled)}
          onMoveUp={() => handleMoveUp(i)}
          onMoveDown={() => handleMoveDown(i)}
          isDragOver={drag.dropIndex === i}
          onDragStart={() => drag.start(rule.id)}
          onDragOver={() => drag.over(i, rule.id)}
          onDrop={() => drag.drop(i)}
          onDragEnd={drag.end}
        />
      ))}
    </div>
  )
}
