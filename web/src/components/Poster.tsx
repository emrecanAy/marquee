import type { CSSProperties } from 'react'
import './Poster.css'

interface PosterProps {
  title: string
  genre: string
  hue: number
}

/**
 * The films are invented, so their posters are too: each is drawn in CSS from the film's hue,
 * with one of three motifs so neighbouring posters do not look alike.
 */
export function Poster({ title, genre, hue }: PosterProps) {
  const motif = ['sun', 'beam', 'arch'][hue % 3]
  return (
    <div className={`poster poster--${motif}`} style={{ '--hue': hue } as CSSProperties} aria-hidden="true">
      <span className="poster__genre">{genre}</span>
      <span className="poster__title">{title}</span>
    </div>
  )
}
