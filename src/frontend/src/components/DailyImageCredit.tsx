import type { DailyImage } from '../hooks/useDailyImage'

/** The title and the copyright, the two lines every credit of the image of the day is made of. */
export function CreditLines({ image }: { image: DailyImage }) {
  return (
    <>
      <strong>{image.title}</strong>
      <span>{image.copyright}</span>
    </>
  )
}

export default function DailyImageCredit({ image, className }: { image: DailyImage; className: string }) {
  return <p className={`daily-credit ${className}`}><CreditLines image={image} /></p>
}
