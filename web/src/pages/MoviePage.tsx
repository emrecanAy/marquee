import { Link, useParams } from 'react-router-dom'
import { useMovie } from '../api/queries'
import type { ShowtimeSlot } from '../api/types'
import { Poster } from '../components/Poster'
import { dayKey, formatDay, formatDuration, formatMoney, formatTime, relativeDay } from '../lib/format'
import './MoviePage.css'

export function MoviePage() {
  const { movieId = '' } = useParams()
  const { data: movie, error } = useMovie(movieId)

  if (error) {
    return (
      <div className="page-state">
        <h1>This film is not on the programme</h1>
        <Link to="/" className="button">
          See what is showing
        </Link>
      </div>
    )
  }

  if (!movie) return null

  const days = groupByDay(movie.showtimes)

  return (
    <article className="movie">
      <div className="movie__poster">
        <Poster title={movie.title} genre={movie.genre} hue={movie.posterHue} />
      </div>

      <div className="movie__body">
        <h1>{movie.title}</h1>
        <p className="movie__tagline">{movie.tagline}</p>

        <dl className="movie__facts">
          <div>
            <dt>Genre</dt>
            <dd>{movie.genre}</dd>
          </div>
          <div>
            <dt>Running time</dt>
            <dd>{formatDuration(movie.durationMinutes)}</dd>
          </div>
          <div>
            <dt>Rating</dt>
            <dd>{movie.ageRating}</dd>
          </div>
          <div>
            <dt>Director</dt>
            <dd>{movie.director}</dd>
          </div>
        </dl>

        <p className="movie__synopsis">{movie.synopsis}</p>

        <section aria-labelledby="showtimes-title" className="movie__showtimes">
          <h2 id="showtimes-title">Showtimes</h2>
          {days.length === 0 ? (
            <p className="muted">No more showings are planned for this film.</p>
          ) : (
            days.map((day) => (
              <div key={day.key} className="movie__day">
                <DayHeading startsAt={day.slots[0]!.startsAt} />
                <ul>
                  {day.slots.map((slot) => (
                    <li key={slot.id}>
                      <Link to={`/showtimes/${slot.id}`} className="movie__slot">
                        <span className="movie__time">{formatTime(slot.startsAt)}</span>
                        <span>{slot.hallName}</span>
                        <span className="muted">from {formatMoney(slot.priceFrom)}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            ))
          )}
        </section>
      </div>
    </article>
  )
}

/** "Today" and "Tomorrow" lead; further out, the date alone says it. */
function DayHeading({ startsAt }: { startsAt: string }) {
  const near = relativeDay(startsAt)
  return near === 'Today' || near === 'Tomorrow' ? (
    <h3>
      {near}
      <span className="muted"> {formatDay(startsAt)}</span>
    </h3>
  ) : (
    <h3>{formatDay(startsAt)}</h3>
  )
}

function groupByDay(showtimes: ShowtimeSlot[]): { key: string; slots: ShowtimeSlot[] }[] {
  const days = new Map<string, ShowtimeSlot[]>()
  for (const slot of showtimes) {
    const key = dayKey(slot.startsAt)
    days.set(key, [...(days.get(key) ?? []), slot])
  }
  return [...days.entries()].map(([key, slots]) => ({ key, slots }))
}
