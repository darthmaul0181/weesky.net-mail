interface Props {
  folderPath: string | null
  uid: number | null
}

export default function MessageReader({ uid }: Props) {
  if (uid === null) return <p className="mail-empty">Select a message</p>

  return <p className="mail-empty">Loading message…</p>
}
