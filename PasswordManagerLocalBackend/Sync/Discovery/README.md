# PasswordManagerLocal local discovery protocol

This protocol replaces the general-purpose mDNS/DNS-SD dependency with the small subset of discovery behavior that PasswordManagerLocal actually needs.

## Security model

The LAN is hostile. Discovery is never an authority.

- A received UDP packet is untrusted until its signature or MAC has been verified.
- Unauthenticated packets must not add endpoints, start synchronization, consume enrollment validation attempts, or mutate throttling/replay state.
- Discovery only produces candidate TCP endpoints. The existing TLS and device/enrollment identity checks remain authoritative.
- Discovery traffic is authenticated but not encrypted. A LAN observer may see that discovery traffic exists and may observe non-secret metadata such as packet timing, device/session identifiers, and query nonces. Secrets, private keys, passwords, and user data are never sent through discovery.
- The UDP multicast TTL is 1, packet size is capped, timestamps must be fresh, and replayed nonces are retained for the complete timestamp-acceptance window.
- Unauthenticated traffic is rejected before it can mutate trusted endpoint, replay, or authenticated-throttling state. The receive loop is sequential, packets are tightly bounded, and expensive authentication is attempted only after cheap message-specific checks.
- Responses do not advertise alternate addresses. They authenticate exactly one responder IPv4 address, and the receiver requires it to equal the observed UDP source address before using it as a candidate. The TCP/TLS layer still validates the endpoint authoritatively.

## Transport

- IPv4 UDP multicast group: `239.255.67.67`
- UDP port: `26689`
- Protocol magic: `PMLD`
- Protocol version: `1`
- Queries are multicast.
- Responses are unicast to the observed query source endpoint.
- There are no persistent advertisements.

## Normal synchronization discovery

1. A sync-enabled device creates a random nonce and multicasts a `SyncQuery` signed with its Ed25519 device key.
2. Only a device that already trusts the requester verifies and answers the query.
3. The response echoes the query nonce, targets the requester device ID, includes the responder device ID, TLS fingerprint, and the responder address selected by the route back to the requester, and is signed by the responder.
4. The requester accepts the response only when:
   - the query nonce is still outstanding;
   - the responder is a trusted, unblocked device;
   - the authenticated responder address exactly equals the observed UDP source address;
   - the TLS fingerprint equals the stored trusted fingerprint;
   - the Ed25519 signature is valid;
   - the response nonce has not already been accepted from that responder.
5. Only then may the endpoint cache and synchronization task service be updated.

## Enrollment discovery

1. The old device, which received the one-time enrollment code, multicasts an `EnrollmentQuery` containing the enrollment session ID and a random nonce.
2. The query is authenticated with HMAC-SHA256 using the enrollment secret from the code.
3. The new device answers only when the session is currently active, unexpired, and the MAC is valid.
4. The response echoes the query nonce and includes the new device identity, TLS fingerprint, public keys, and the responder address selected by the route back to the requester. The entire response is authenticated with the same enrollment secret.
5. The old device accepts the response only for an outstanding nonce, after MAC verification, and when the authenticated responder address exactly equals the observed UDP source address.
6. The normal TCP enrollment flow still performs its existing TLS, code-proof, identity, and transfer validation. UDP discovery does not consume the three enrollment validation attempts.

## Packet framing

All integer fields are encoded with the backend's current `BinaryWriter`/`BinaryReader` representation. Backward compatibility is intentionally not required during development.

Common prefix:

1. 4-byte `PMLD` magic
2. 1-byte protocol version
3. 1-byte message type

Message types:

- `1`: SyncQuery
- `2`: SyncResponse
- `3`: EnrollmentQuery
- `4`: EnrollmentResponse

Every message is authenticated over the complete prefix and payload bytes before its trailing authenticator. Sync messages use a 64-byte Ed25519 signature. Enrollment messages use a 32-byte HMAC-SHA256 value.

## Maintenance invariants

When changing this protocol:

- authenticate before mutating state;
- keep packet and collection bounds explicit;
- keep response correlation through unpredictable nonces;
- reject replays;
- never make UDP discovery a replacement for TLS or identity validation;
- preserve secret zeroing for temporary enrollment-secret copies;
- test malformed packets and hostile ordering, not only successful discovery.
