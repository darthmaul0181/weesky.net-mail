interface Props {
  folderPath: string | null
  selectedUid: number | null
  onSelect: (uid: number) => void
}

export default function MessageList({ folderPath }: Props) {
  if (!folderPath) return <p className="mail-empty">Select a folder</p>

  return <p className="mail-empty">Loading messages…</p>
}
