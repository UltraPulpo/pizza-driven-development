export interface MenuDefinition {
  sizes: { id: string; name: string; basePriceCents: number }[]
  categories: { id: string; percentage: number }[]
  toppings: { id: string; name: string; categoryId: string }[]
}

export interface LineItemInput {
  sizeId: string
  quantity: number
  toppingIds: string[]
}

export interface TipInput {
  mode: 'dollars' | 'percent'
  value: string
}

export interface OrderInput {
  items: LineItemInput[]
  tip: TipInput
  promoCode: string | null
}

export interface PriceQuote {
  lines: {
    sizeId: string
    sizeName: string
    basePriceCents: number
    toppings: { id: string; name: string; chargeCents: number }[]
    unitPriceCents: number
    quantity: number
    lineTotalCents: number
  }[]
  subtotalCents: number
  discountCents: number
  taxableSubtotalCents: number
  taxCents: number
  tipCents: number
  totalCents: number
  description: string
}

export type OrderStatus = 'NotStarted' | 'Started' | 'ReadyForPickup' | 'Delivered' | 'Cancelled'

export interface SubmissionReceipt {
  orderId: string
  quote: PriceQuote
}

export interface OrderView {
  orderId: string
  status: OrderStatus
  order: OrderInput
  quote: PriceQuote
}

export interface SubmissionAttempt {
  readonly key: string
  readonly body: string
}
