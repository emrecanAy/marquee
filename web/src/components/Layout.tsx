import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { signOut } from '../api/client'
import { useCan, useMe } from '../api/queries'
import './Layout.css'

export function Layout() {
  const me = useMe().data
  const location = useLocation()
  const staffLinks = [
    { to: '/programme', label: 'Programme', allowed: useCan('catalog.schedule') },
    { to: '/activity', label: 'Audit trail', allowed: useCan('audit.read') },
    { to: '/staff', label: 'Staff', allowed: useCan('staff.manage') },
  ].filter((link) => link.allowed)

  return (
    <div className="layout">
      <header className="layout__header">
        <Link to="/" className="layout__wordmark" aria-label="Marquee, home">
          Marquee
        </Link>
        <nav aria-label="Main" className="layout__nav">
          {staffLinks.map((link) => (
            <NavLink key={link.to} to={link.to} className="layout__link layout__link--staff">
              {link.label}
            </NavLink>
          ))}
          <NavLink to="/tickets" className="layout__link">
            My tickets
          </NavLink>
          {me?.account ? (
            <>
              <NavLink to="/account" className="layout__link" title={me.account.email}>
                {me.account.fullName.split(' ')[0]}
              </NavLink>
              <button type="button" className="layout__link layout__sign-out" onClick={() => void signOut()}>
                Sign out
              </button>
            </>
          ) : (
            <NavLink to={`/sign-in?next=${encodeURIComponent(location.pathname)}`} className="layout__link">
              Sign in
            </NavLink>
          )}
        </nav>
      </header>

      <main className="layout__main">
        <Outlet />
      </main>

      <footer className="layout__footer">
        <p>A demo cinema. Cards are never charged and the films do not exist.</p>
      </footer>
    </div>
  )
}
