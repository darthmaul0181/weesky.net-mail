/** The product's own name, written here rather than read from a setting: the administrator's
    `app.name` titles the browser tab and the installed app, which is a different question.
    Two parts because both the top bar and the About page draw them in two weights. */
export const PRODUCT = { name: 'Scotty', kind: 'webmail' } as const

export const PRODUCT_NAME = `${PRODUCT.name} ${PRODUCT.kind}`
