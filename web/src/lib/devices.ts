/** A readable name for a user agent string; the full string means little to most people. */
export function describeDevice(userAgent: string | null): string {
  if (!userAgent) return 'Unknown device'
  const browser = /Edg\//.test(userAgent)
    ? 'Edge'
    : /Firefox\//.test(userAgent)
      ? 'Firefox'
      : /Chrome\//.test(userAgent)
        ? 'Chrome'
        : /Safari\//.test(userAgent)
          ? 'Safari'
          : 'Browser'
  const system = /iPhone|iPad/.test(userAgent)
    ? 'iPhone/iPad'
    : /Android/.test(userAgent)
      ? 'Android'
      : /Mac OS X/.test(userAgent)
        ? 'Mac'
        : /Windows/.test(userAgent)
          ? 'Windows'
          : /Linux/.test(userAgent)
            ? 'Linux'
            : 'unknown system'
  return `${browser}, ${system}`
}
