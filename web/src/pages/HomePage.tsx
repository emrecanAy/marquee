import { Link } from 'react-router-dom'
import { useMovies } from '../api/queries'
import { MarqueeSign } from '../components/MarqueeSign'
import { Poster } from '../components/Poster'
import { formatDuration } from '../lib/format'
import './HomePage.css'

export function HomePage() {
  const { data: movies, error } = useMovies()

  if (error) {
    return (
      <div className="page-state">
        <h1>The box office is not answering</h1>
        <p className="muted">The programme could not be loaded. Check that the API is running, then reload.</p>
      </div>
    )
  }

  if (!movies) return null

  return (
    <>
      <MarqueeSign movies={movies} />

      <section className="programme" aria-labelledby="programme-title">
        <h2 id="programme-title">This week's programme</h2>
        <ul className="programme__grid">
          {movies.map((movie) => (
            <li key={movie.id}>
              <Link to={`/movies/${movie.id}`} className="programme__film">
                <Poster title={movie.title} genre={movie.genre} hue={movie.posterHue} />
                <span className="programme__name">{movie.title}</span>
                <span className="muted">
                  {formatDuration(movie.durationMinutes)}, rated {movie.ageRating}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      </section>
    </>
  )
}
