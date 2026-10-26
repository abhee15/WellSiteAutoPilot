# Runtime broker authentication

The bundled broker requires a deployment-specific 256-bit random token. Server,
Gateway and .NET worker receive it through their existing account-protected
settings files as `Messaging:Nats:Token`. The HTTP caller keys remain distinct;
the Gateway still stores only verifiers for those caller keys.

The installer writes `nats.auth.conf` under the product ProgramData root, readable
by SYSTEM, Administrators and LocalService. NATSHost passes the configuration
file path to nats-server; the token is not placed in its command line or URL.
NATSHost refuses to start if this file is missing. Existing installations acquire
a token on the next install/upgrade; subsequent installs preserve it, and
`--rotate-service-keys` regenerates both caller credentials and broker token.

All three runtime accounts are trusted broker participants and share this token.
It prevents other local processes from publishing requests/results or replacing
package objects without a credential. Per-service subject ACLs and execution of
untrusted customer code require a separate design; this is the current trusted
on-premises runtime boundary.

The package source additionally refuses a broker whose server information does
not require authentication, returning `MODULE_PACKAGE_BROKER_AUTH_REQUIRED`
before retrieving bytes. A development anonymous broker can still serve legacy
test traffic, but cannot supply executable packages to the verified host.

Aspire generates a fresh broker credential and owner-protected configuration
file for an internally started broker. External mode takes `Nats:Token` from
developer configuration/user-secrets. Explicit credential environment values
override installed credential files; unrelated authentication settings retain
their existing precedence. Server-to-Gateway credentials are also supplied by
the AppHost. No broker token is checked into application configuration.

Verification covers anonymous and wrong-token wire-level rejection, an
authenticated topology/Object Store round trip across broker restart, rejection
of an anonymous package source, protected-file serializers and credential-only
environment precedence. Windows installer smoke reads its generated credential
and runs the same broker acceptance/rejection checks against the installed
service. CI test tokens are ephemeral and masked.
