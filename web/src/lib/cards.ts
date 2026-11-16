/**
 * Stand-in for a payment provider's browser SDK, which exchanges card details for a one-time token
 * so that card numbers never reach our API. Like a provider's sandbox, specific test numbers
 * produce specific outcomes.
 */
export const TEST_CARDS = [
  { number: '4242 4242 4242 4242', outcome: 'Payment succeeds', token: 'tok_approved' },
  { number: '4000 0000 0000 0002', outcome: 'Card is declined', token: 'tok_declined' },
  { number: '4000 0000 0000 0259', outcome: 'Succeeds after a slow bank reply', token: 'tok_slow' },
] as const

export type Tokenized = { ok: true; token: string } | { ok: false; reason: string }

export function tokenizeCard(cardNumber: string): Tokenized {
  const digits = cardNumber.replace(/\D/g, '')
  if (digits.length !== 16) return { ok: false, reason: 'Enter the 16 digits of the card number.' }

  const known = TEST_CARDS.find((card) => card.number.replace(/\s/g, '') === digits)
  if (known) return { ok: true, token: known.token }

  return passesLuhn(digits)
    ? { ok: true, token: 'tok_approved' }
    : { ok: false, reason: 'This card number is not valid. Check the digits.' }
}

/** Formats input as the user types: groups of four, digits only. */
export function formatCardNumber(input: string): string {
  return (input.replace(/\D/g, '').slice(0, 16).match(/.{1,4}/g) ?? []).join(' ')
}

function passesLuhn(digits: string): boolean {
  let sum = 0
  for (let i = 0; i < digits.length; i++) {
    let digit = Number(digits[digits.length - 1 - i])
    if (i % 2 === 1) {
      digit *= 2
      if (digit > 9) digit -= 9
    }
    sum += digit
  }
  return sum % 10 === 0
}
