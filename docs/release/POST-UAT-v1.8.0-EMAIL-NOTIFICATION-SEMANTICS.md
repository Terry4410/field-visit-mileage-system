# v1.8.0 Email Notification Semantics

Status: requirement/design correction only. No email provider, sender, credential, allowlist entry or send operation is authorized.

## Business rule

Transaction workflow notifications are part of the controlled business process and cannot be disabled by an individual's email preference. Reminder/optional notifications may be disabled by the individual.

| Notification class | Example | Personal preference applies? | Environment policy applies? |
|---|---|---:|---:|
| Transaction | Trip submitted/approved/returned; correction; Location review decision | No | Yes |
| Reminder | Authorization/site/project expiry reminder | Yes | Yes |
| System | Import completion/failure to initiator | No in v1.8.0 | Yes |

The personal field is named `OptionalEmailNotificationEnabled`. It must not be displayed or described as disabling all email. UI wording must state that workflow/transaction messages can still be sent.

## Dispatch decision order

1. A committed business transaction creates its outbox record in the same transaction.
2. Environment policy is evaluated first. `Disabled` or `IsEnabled = 0` prevents delivery.
3. In Test mode, only an active allowlisted address may be delivered.
4. Event configuration/recipient rule is evaluated.
5. `OptionalEmailNotificationEnabled` is evaluated only when `HonorsOptionalPreference = 1`; in the seeded model this means Reminder events.
6. Transaction/System delivery is not suppressed by the optional preference.
7. Provider failure is logged and never rolls back the already committed business transaction.

## UAT invariant

`1800_006` seeds `EnvironmentCode=UAT`, `EmailMode=Test`, `IsEnabled=0`, with an empty allowlist. The Verify script fails if UAT is Live, enabled contrary to the seed expectation, has an active initial allowlist, or if Transaction events are configured to honor the optional preference.

Activating Test mode, adding allowlist entries or configuring a sender/provider requires a separate approval and must not be performed by migration.

## Test gate

- Transaction notification is queued regardless of personal optional preference.
- Reminder is suppressed when the preference is off and queued when on.
- UAT Disabled prevents all actual delivery.
- UAT Test rejects non-allowlisted recipients.
- Empty allowlist produces no delivery.
- Delivery failure does not change Trip/approval state.
