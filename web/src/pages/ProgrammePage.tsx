import { type FormEvent, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, postJson } from '../api/client'
import { useMovies } from '../api/queries'
import type { Hall, PlannedShowtime } from '../api/types'
import { formatDay, formatMoney, formatTime } from '../lib/format'
import './ProgrammePage.css'

/** Programmers put films in halls. The API refuses a showing that overlaps another in the same hall. */
export function ProgrammePage() {
  const queryClient = useQueryClient()
  const { data: movies } = useMovies()
  const { data: halls } = useQuery({ queryKey: ['programme', 'halls'], queryFn: () => api<Hall[]>('/programme/halls') })
  const { data: planned } = useQuery({
    queryKey: ['programme', 'showtimes'],
    queryFn: () => api<PlannedShowtime[]>('/programme/showtimes'),
  })

  const [movieId, setMovieId] = useState('')
  const [hallId, setHallId] = useState('')
  const [startsAt, setStartsAt] = useState('')
  const [price, setPrice] = useState('180')
  const [added, setAdded] = useState<PlannedShowtime>()

  const schedule = useMutation({
    mutationFn: () =>
      postJson<PlannedShowtime>('/programme/showtimes', {
        movieId,
        hallId,
        // The input is the cinema's local time; the API stores the instant.
        startsAt: new Date(startsAt).toISOString(),
        basePrice: Number(price.replace(',', '.')),
      }),
    onSuccess: (showtime) => {
      setAdded(showtime)
      void queryClient.invalidateQueries({ queryKey: ['programme'] })
      void queryClient.invalidateQueries({ queryKey: ['movies'] })
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setAdded(undefined)
    schedule.mutate()
  }

  return (
    <div className="programme">
      <header>
        <h1>Programme</h1>
        <p className="muted">A new showing goes on sale as soon as it is saved. Each hall needs 20 minutes between showings.</p>
      </header>

      <form className="form panel programme__form" onSubmit={submit}>
        <label className="field">
          Film
          <select required value={movieId} onChange={(e) => setMovieId(e.target.value)}>
            <option value="" disabled>
              Choose a film
            </option>
            {movies?.map((movie) => (
              <option key={movie.id} value={movie.id}>
                {movie.title} ({movie.durationMinutes} min)
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          Hall
          <select required value={hallId} onChange={(e) => setHallId(e.target.value)}>
            <option value="" disabled>
              Choose a hall
            </option>
            {halls?.map((hall) => (
              <option key={hall.id} value={hall.id}>
                {hall.name} ({hall.seats} seats)
              </option>
            ))}
          </select>
        </label>
        <div className="field-row">
          <label className="field">
            Starts
            <input type="datetime-local" required value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
          </label>
          <label className="field">
            Base price <small>Premium rows cost more</small>
            <input inputMode="decimal" required value={price} onChange={(e) => setPrice(e.target.value)} />
          </label>
        </div>
        {schedule.error && (
          <p className="form-error" role="alert">
            {schedule.error.message}
          </p>
        )}
        {added && (
          <p className="notice programme__added" role="status">
            {added.movieTitle} is on sale: {formatDay(added.startsAt)} {formatTime(added.startsAt)}, {added.hallName}.
          </p>
        )}
        <div className="form-actions">
          <button type="submit" className="button" disabled={schedule.isPending}>
            Put on sale
          </button>
        </div>
      </form>

      <section>
        <h2>Coming up</h2>
        <ul className="programme__list">
          {planned?.map((showtime) => (
            <li key={showtime.id}>
              <span className="programme__when">
                {formatDay(showtime.startsAt)} {formatTime(showtime.startsAt)}–{formatTime(showtime.endsAt)}
              </span>
              <strong>{showtime.movieTitle}</strong>
              <span className="muted">{showtime.hallName}</span>
              <span className="muted">from {formatMoney(showtime.basePrice)}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}
