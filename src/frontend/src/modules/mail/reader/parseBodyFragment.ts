// An explicit <body> keeps a leading <style> inside it; without one the parser hoists it into
// <head>, and a caller reading doc.body.innerHTML back out loses it.
export function parseBodyFragment(html: string): Document {
  return new DOMParser().parseFromString('<body>' + html, 'text/html')
}
