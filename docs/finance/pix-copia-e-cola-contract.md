# PIX Copia e Cola on payment plans — contract

Applies to every place an installment can carry a PIX payment: títulos a pagar (`POST /api/payables`, `PUT /api/payables/{id}`), requisition payment plans (create, update, `submit-financial-complement`) and Conta Fixa cycles. This document is authoritative: where the code disagrees, the code is wrong.

## Terms

- **PIX key**: a vendor's addressing key (CPF/CNPJ, e-mail, phone, EVP). It is an address. Paying the same key many times is normal.
- **Copia e cola**: an EMV® BR Code payload (Bacen) — the text behind a PIX QR code. It always starts with `000201`.
- A BR Code is **static** or **dynamic**, as stated by the code itself: the Point of Initiation Method (tag `01`) is `11` for static and `12` for dynamic. When tag `01` is absent, a payload-location URL (merchant-account sub-tag `25`) means dynamic, and a key (sub-tag `01`) means static.
  - A **static** BR Code wraps the vendor's PIX key. It is reusable, exactly like the key. It is an address, not a charge.
  - A **dynamic** BR Code points at one cobrança issued by the vendor's bank. It is a charge, like a boleto linha digitável: paying it twice pays the vendor twice.

## Rules

**CP1 — A dynamic copia e cola is paid once.** While a título or requisition that holds a dynamic copia e cola is open, no other installment may carry the same copia e cola (compared after trimming), neither in the same request nor in any other título, requisition plan or Conta Fixa cycle, across companies. The refusal is `409` with `payable.pix_payload_emv_duplicate` (títulos) or `requisition.pix_payload_emv_duplicate` (requisitions and cycles). As for boletos: a cancelled/rejected título or a cancelled requisition releases it, and an aggregate never conflicts with its own lines (including a título with the requisition it came from).

**CP2 — A static copia e cola is an address.** It never triggers a duplicate refusal: the same static copia e cola may repeat on several installments of one request, and on any number of títulos, plans and cycles.

**CP3 — A copia e cola is a copia e cola wherever it is typed.** The forms have a single PIX field, sent as `pixKeyOverride`. When the value sent as the PIX key is a BR Code (it starts with `000201`) and no explicit `pixPayloadEmv` is sent for that installment, it is stored as the installment's copia e cola: `pixPayloadEmv` holds the payload and `pixKeyOverride` stays empty. It is never stored as a key, never truncated, never rejected for being long. Once stored, it is a copia e cola like any other, so CP1 and CP2 apply to every later installment that carries it. On títulos (create and update), the copia-e-cola rules also apply to the request itself exactly as if the value had been sent as `pixPayloadEmv`: validity (structure and CRC), CP1 and CP2.

**CP4 — Plain PIX keys are unaffected.** A value that is not a BR Code stays in `pixKeyOverride` and is never checked for duplicates. Sending both a key and an explicit copia e cola on one installment stays a `400`.

**CP5 — Errors are client errors.** A malformed copia e cola is a `400` (`*.pix_payload_emv_invalid`); a duplicate is a `409`. Nothing in this contract may surface as a `500`.
