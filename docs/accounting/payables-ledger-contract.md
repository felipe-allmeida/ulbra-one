# Payables in the ledger — contract

Status: **authoritative**. Where the code disagrees with this document, the document wins.

Finance owns the payable (título a pagar). Accounting books what happens to it in the per-company general ledger by consuming the Finance events listed below. This document states what the ledger must show. It does not prescribe how the code achieves it.

## Events (published by Finance, consumed by Accounting)

| Event | Business meaning |
|---|---|
| `PayableScheduled` | The title was approved for payment, possibly again after a return. It carries the current classification (expense account), which may be null when the title is not classified yet. |
| `PayableReclassified` | Contábil set or changed the title's expense account after it was scheduled. It carries the new account. |
| `PaymentRegistered` | A payment (baixa) was made. |
| `PayableCancelled` | The title was cancelled. |
| `PayableRejected` | Finance returned the title (devolução) to Budgeting for correction. A returned title can later be corrected and re-approved, which publishes `PayableScheduled` again for the same title. |

Delivery is at-least-once: any event can be delivered more than once.

## What the ledger must show

**L1. Provision (provisão) of a live title.** While a title is scheduled or paid, the ledger holds exactly one effective provision for it:
- a debit to its current expense account;
- a credit to Fornecedores a Pagar (`Accounting:PayablesLiabilityCode`).

Both are for the title's gross amount, net of withholdings as configured. When the title has no usable expense account, the provision is booked to the suspense account (`Accounting:UnclassifiedExpenseCode`) if one is configured. Otherwise it waits as a pending posting (L4).

**L2. Reclassification moves the expense only.** After `PayableReclassified`, the effective provision debits the new account and no longer the old one. Fornecedores a Pagar is unchanged.

**L3. Cancelled and returned titles have no provision.** After `PayableCancelled` or `PayableRejected`, the title's net effect on every expense account and on Fornecedores a Pagar is zero. This holds no matter how many times it was reclassified or re-approved before. Corrections are made by reversal (estorno); posted entries are never edited or deleted. If nothing was booked, there is nothing to reverse. When a returned title is re-approved, its provision is booked again (L1).

**L4. Pending postings converge.** A provision that cannot be booked yet is held as at most one open pending posting per title. When the title gets a usable account (for example through `PayableReclassified`), the provision is booked and that pending posting is resolved. A title must never end up with neither a provision nor an open pending posting while it is scheduled or paid. It must never keep an open pending posting after its provision is booked.

**L5. Payment.** `PaymentRegistered` debits Fornecedores a Pagar and credits cash. A fully paid title nets to zero on Fornecedores a Pagar.

**L6. Idempotency.** Redelivering any event does not change the ledger.

**L7. Every entry balances.** Σdebits = Σcredits on every entry.
