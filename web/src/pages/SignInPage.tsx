import { type FormEvent, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { authenticate } from '../api/client'
import type { SignInResult } from '../api/types'
import './SignInPage.css'

type Mode = 'sign-in' | 'register'

/**
 * Signing in is optional: tickets can be bought as a guest. An account keeps them on every device,
 * and whatever was picked or bought in this browser before signing in comes along.
 */
export function SignInPage() {
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const next = safeNext(params.get('next'))
  const [mode, setMode] = useState<Mode>(params.get('mode') === 'register' ? 'register' : 'sign-in')
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [ticket, setTicket] = useState<string>()
  const [code, setCode] = useState('')

  const submit = useMutation({
    mutationFn: () =>
      ticket
        ? authenticate<SignInResult>('/sign-in/second-step', { ticket, code })
        : mode === 'register'
          ? authenticate<SignInResult>('/register', { fullName, email, password })
          : authenticate<SignInResult>('/sign-in', { email, password }),
    onSuccess: (result) => {
      if (result.twoStepTicket) {
        setTicket(result.twoStepTicket)
        return
      }
      navigate(next, { replace: true })
    },
    onError: (error) => {
      if ('code' in error && error.code === 'ticket_expired') setTicket(undefined)
    },
  })

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    submit.mutate()
  }

  return (
    <div className="sign-in">
      <div className="sign-in__intro">
        <h1>{ticket ? 'One more step' : mode === 'register' ? 'Keep your tickets' : 'Welcome back'}</h1>
        <p className="muted">
          {ticket
            ? 'Enter the six-digit code from your authenticator app, or one of your recovery codes.'
            : 'You can buy tickets without an account. With one, they are on every device you sign in on, and seats you picked in this browser come with you.'}
        </p>
      </div>

      <form className="form panel" onSubmit={onSubmit}>
        {ticket ? (
          <label className="field">
            Code
            <input autoComplete="one-time-code" autoFocus required value={code} onChange={(e) => setCode(e.target.value)} />
          </label>
        ) : (
          <>
            {mode === 'register' && (
              <label className="field">
                Name
                <input autoComplete="name" required value={fullName} onChange={(e) => setFullName(e.target.value)} />
              </label>
            )}
            <label className="field">
              E-mail
              <input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
            </label>
            <label className="field">
              Password {mode === 'register' && <small>At least 12 characters. A short sentence works well.</small>}
              <input
                type="password"
                autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
                minLength={mode === 'register' ? 12 : undefined}
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
              />
            </label>
          </>
        )}

        {submit.error && (
          <p className="form-error" role="alert">
            {submit.error.message}
          </p>
        )}

        <div className="form-actions">
          <button type="submit" className="button" disabled={submit.isPending}>
            {ticket ? 'Verify' : mode === 'register' ? 'Create account' : 'Sign in'}
          </button>
          {!ticket && (
            <button
              type="button"
              className="button button--quiet"
              onClick={() => {
                submit.reset()
                setMode(mode === 'register' ? 'sign-in' : 'register')
              }}
            >
              {mode === 'register' ? 'I have an account' : 'Create an account'}
            </button>
          )}
        </div>

        {!ticket && (
          <p className="muted sign-in__demo">
            Demo accounts, password <code>marquee-demo-2026</code>: <code>seyirci@marquee.local</code> (customer),{' '}
            <code>programci@marquee.local</code> (programmer), <code>denetci@marquee.local</code> (auditor). Administrator:{' '}
            <code>admin@marquee.local</code>, <code>marquee-platform-2026</code>.
          </p>
        )}
      </form>

      <Link to={next} className="sign-in__back">
        Continue without signing in
      </Link>
    </div>
  )
}

/** Only a path on this site, so a crafted link cannot send someone elsewhere after signing in. */
export function safeNext(next: string | null): string {
  return next && next.startsWith('/') && !next.startsWith('//') ? next : '/'
}
