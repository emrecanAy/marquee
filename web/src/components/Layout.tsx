import { Link, NavLink, Outlet } from 'react-router-dom'
import './Layout.css'

export function Layout() {
  return (
    <div className="layout">
      <header className="layout__header">
        <Link to="/" className="layout__wordmark" aria-label="Marquee, home">
          Marquee
        </Link>
        <nav aria-label="Main">
          <NavLink to="/tickets" className="layout__link">
            My tickets
          </NavLink>
        </nav>
      </header>

      <main className="layout__main">
        <Outlet />
      </main>

      <footer className="layout__footer">
        <p>A demo cinema. Cards are never charged and the films do not exist.</p>
        <Link to="/activity">Database activity</Link>
      </footer>
    </div>
  )
}
