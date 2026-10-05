import type { MailFolderNode, MailFolderPage, MailMessageSummary } from './api/mailTypes'

export function summaryOf(uid: number, over: Partial<MailMessageSummary> = {}): MailMessageSummary {
  return {
    uid, subject: 's', fromName: 'n', fromAddress: 'a@b.c', to: [], date: '2026-07-22T10:00:00Z',
    seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
    priority: 'normal', ...over,
  }
}

export function pageOf(messages: MailMessageSummary[], over: Partial<MailFolderPage> = {}): MailFolderPage {
  return { folderPath: 'INBOX', uidValidity: 1, total: 20, page: 0, pageSize: 50, messages, ...over }
}

export function folderNodeOf(fields: Partial<MailFolderNode> & { path: string }): MailFolderNode {
  return {
    name: fields.path, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...fields,
  }
}
