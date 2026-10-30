# Local Stripe sandbox setup

The local integration uses Stripe-hosted Checkout and Stripe.net 53.1.0. It rejects live API keys and live webhook events. Automated tests use a fake provider and real PostgreSQL; those tests do not prove account access or a real Stripe checkout.

Reconnecting the Stripe chat app does not configure the running .NET backend. Keep these credentials in the ignored `.env` beside `compose.yaml`, never in chat or source control.

1. In Stripe, create/select a separate development sandbox. Prefer a restricted **test** key (`rk_test_`) with Checkout Sessions read/write, Refunds write and PaymentIntents read permissions. Set `STRIPE_API_KEY` locally. Use only the permissions needed by the integration; check rejected operations in the sandbox request logs.
2. Configure a payment-method configuration for immediate card payments and set its ID as `STRIPE_PAYMENT_METHOD_CONFIGURATION`. This portfolio demo uses sandbox card payments. The webhook also safely handles a delayed success, but delayed methods are outside the demonstrated checkout journey.
3. Log the Stripe CLI into that same sandbox, then run:

   ```powershell
   stripe listen --events checkout.session.completed,checkout.session.async_payment_succeeded,checkout.session.expired --forward-to http://localhost:8080/api/payments/webhook
   ```

   A workspace-local CLI is available at `../../work/runtime/stripe/stripe.exe` in this development workspace. Use that path in place of `stripe` if it is not on PATH. The listener must stay running during local checkout.
4. Save the listener's signing secret as `STRIPE_WEBHOOK_SECRET` in `.env`. Do not paste it into chat. Run `docker compose up -d --build app` to reload local configuration.
5. Sign in, reserve a paid fictional event and open sandbox checkout. Use Stripe's documented test card `4242 4242 4242 4242`, a future expiry and any test CVC. Never enter a real card. The return page reads the booking; only a verified webhook issues tickets.

Before calling this integration demonstrated, verify a real sandbox payment, repeated callback delivery, cancellation/expiry, and a worker restart. These external checks are still pending.

## Recovery behaviour

- Every booking has one persisted checkout key and immutable checkout parameters. Ambiguous responses reuse that key; a definitive invalid-request rejection cancels the hold. Checkout is created after committing inventory changes.
- Holds last 35 minutes. Stripe requires 30 minutes at session creation. A new checkout cannot start when fewer than 31 minutes remain; cancel the old hold and reserve again. Existing attempts retain their original parameters for reconciliation.
- Notifications are signed, bounded to 256 KiB, sandbox-only and checked against booking/session/payment IDs, amount and currency. Duplicate IDs or duplicate payments never issue extra tickets. Expiration notifications do not undo confirmation.
- Late successful payments reacquire available capacity. If capacity has gone, the event has started or the customer cancelled, the booking enters `PaymentReview`, receives no tickets and queues a durable refund.
- A refund worker uses a five-minute lease and a stable provider key across retries and restarts. Refunds that remain ambiguous for 23 hours, or return a non-success status, enter `NeedsReview` instead of risking a new refund after Stripe's idempotency retention window. The operator must reconcile these in the sandbox dashboard. An automated reconciliation dashboard is outside this milestone.
- Free bookings confirm locally under the same inventory lock without contacting Stripe.

For AWS, store payment credentials in SSM Parameter Store SecureString or Secrets Manager with scoped instance permissions; local `.env` files are not the deployment secret store.

References: [Checkout inventory expiry](https://docs.stripe.com/payments/checkout/managing-limited-inventory?ui=full-page), [fulfilment](https://docs.stripe.com/checkout/fulfillment), [webhook signatures](https://docs.stripe.com/webhooks), [idempotency](https://docs.stripe.com/api/idempotent_requests), [sandbox testing](https://docs.stripe.com/testing), [restricted keys](https://docs.stripe.com/keys).
