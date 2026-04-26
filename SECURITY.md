# Security Policy

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, use GitHub's private vulnerability reporting:

1. Go to the **Security** tab of this repository.
2. Click **Report a vulnerability**.
3. Fill in the advisory form.

This creates a private security advisory visible only to the repository maintainers.

## What to include

Because FluentRegedit reads and modifies the **Windows registry**, even small bugs can have serious consequences (privilege escalation, persistent system damage, data loss). When reporting, please include:

- A clear, minimal **reproduction** — exact steps, registry path(s) involved, and value contents.
- The **impact** you observed or believe is possible (e.g. unintended write outside the targeted key, elevation, denial of service, leakage of HKLM data to a non-admin user).
- The **environment**: Windows version & build, FluentRegedit version/commit, whether the app was running elevated.
- Any proof-of-concept artifacts (`.reg` files, screenshots, crash dumps) — attach them privately to the advisory, not to a public issue.

## Disclosure

Maintainers will acknowledge receipt, investigate, and coordinate a fix and disclosure timeline with you privately. Please give us a reasonable window to ship a fix before any public disclosure.

Thank you for helping keep FluentRegedit and its users safe.
