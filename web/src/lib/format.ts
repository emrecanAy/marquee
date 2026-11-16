const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', maximumFractionDigits: 2 })
const clock = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', minute: '2-digit' })
const weekday = new Intl.DateTimeFormat('en-GB', { weekday: 'short' })
const longDay = new Intl.DateTimeFormat('en-GB', { weekday: 'long', day: 'numeric', month: 'long' })

export function formatMoney(amount: number): string {
  return money.format(amount).replace(/,00$/, '')
}

export function formatTime(iso: string): string {
  return clock.format(new Date(iso))
}

export function formatDay(iso: string): string {
  return longDay.format(new Date(iso))
}

/** "Today", "Tomorrow", or the short weekday: how a box office talks about the next few days. */
export function relativeDay(iso: string, now = new Date()): string {
  const date = new Date(iso)
  const days = Math.round((startOfDay(date) - startOfDay(now)) / 86_400_000)
  if (days === 0) return 'Today'
  if (days === 1) return 'Tomorrow'
  return weekday.format(date)
}

export function dayKey(iso: string): string {
  const date = new Date(iso)
  return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`
}

export function formatDuration(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return hours === 0 ? `${rest} min` : `${hours} h ${rest.toString().padStart(2, '0')} min`
}

/** mm:ss until the given moment, never below zero. */
export function formatCountdown(untilIso: string, now: number): string {
  const seconds = Math.max(0, Math.ceil((new Date(untilIso).getTime() - now) / 1000))
  return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`
}

function startOfDay(date: Date): number {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime()
}
