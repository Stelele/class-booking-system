# Twilio production sender checklist (one-time, human)

1. Twilio Console → Messaging → Senders → buy/enable a WhatsApp sender (pay-as-you-go).
2. Submit 4 templates for approval (exact bodies live in
   `backend/Booking.Application/Notifications/ReminderMessages.cs`):
   confirmation, cancellation, monday-summary, morning-nudge, evening-nudge.
   Record returned SIDs in `Twilio:TemplateSids` config when enforcing templates.
3. `gh secret set TWILIO_ACCOUNT_SID / TWILIO_AUTH_TOKEN / TWILIO_FROM_NUMBER` (E.164, e.g. +15551234567).
4. Set real phones: `POST /api/admin/users/phone` per user (teacher + 2 students) — or direct DB update.
5. Deploy → verify in `ReminderLog` (`GET` via admin? check logs) + one live confirmation booking.
6. Sandbox remains for dev (leave `Twilio:*` empty locally → log-only sender).
