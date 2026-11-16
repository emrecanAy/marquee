import { Link } from 'react-router-dom'
import type { MovieSummary } from '../api/types'
import { formatTime, relativeDay } from '../lib/format'
import './MarqueeSign.css'

const BULBS = 30

/**
 * The cinema's street sign: a backlit letterboard framed by bulbs, listing what plays next.
 * It is also the fastest way in, since every line links to its film.
 */
export function MarqueeSign({ movies }: { movies: MovieSummary[] }) {
  const playing = movies
    .filter((movie) => movie.nextShowtime !== null)
    .sort((a, b) => a.nextShowtime!.localeCompare(b.nextShowtime!))

  return (
    <section className="sign" aria-labelledby="sign-title">
      <BulbRow />
      <div className="sign__board">
        <h1 id="sign-title" className="sign__title">
          Now showing
        </h1>
        {playing.length === 0 ? (
          <p className="sign__empty">The programme is being printed. Check back in a moment.</p>
        ) : (
          <ol className="sign__lines">
            {playing.map((movie) => (
              <li key={movie.id}>
                <Link to={`/movies/${movie.id}`} className="sign__line">
                  <span className="sign__film">{movie.title}</span>
                  <span className="sign__leader" aria-hidden="true" />
                  <span className="sign__time">
                    {relativeDay(movie.nextShowtime!)} {formatTime(movie.nextShowtime!)}
                  </span>
                </Link>
              </li>
            ))}
          </ol>
        )}
      </div>
      <BulbRow />
    </section>
  )
}

function BulbRow() {
  return (
    <div className="sign__bulbs" aria-hidden="true">
      {Array.from({ length: BULBS }, (_, index) => (
        <span key={index} style={{ animationDelay: `${index * 45}ms` }} />
      ))}
    </div>
  )
}
