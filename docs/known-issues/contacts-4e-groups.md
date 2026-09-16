# Contacts 4e — groups: what the slice left open

## A client scenario never run

Group synchronisation was never exercised against a real client, and it **cannot be an
observation**: no group exists in the database to watch, so one has to be *created* on each side.

| Step | DAVx⁵ | iOS Contacts | Thunderbird |
|---|---|---|---|
| Group created in the webmail, seen on the device | to run | to run | not applicable |
| Group created on the device, seen in the webmail | to run | to run (iOS 16+ "lists") | not playable |
| Member added, each direction | to run | to run | not applicable |
| Group deleted, each direction | to run | to run | not applicable |

Thunderbird does not map vCard groups onto its mailing lists — the card arrives as a plain
contact. That is a limitation of the client, not a defect of the server, so "created on the
client" is not playable there.

Design intent: [`../history/specs/2026-08-31-webmail-contacts-4e-groups-design.md`](../history/specs/2026-08-31-webmail-contacts-4e-groups-design.md).

## Nothing from Apple has been observed on this server

What the 4e spec says about Apple — the dialect it understands, the `N` it writes, the dangling
reference it leaves — is general knowledge, not something a test pass established. It stays a
watch list until an Apple device actually presents itself.
