# 0002 Avoid Direct Azure Access

## Status

Accepted

## Context

The product goal is to let DfE users create and manage prototype hosting environments without needing Azure portal access. Allowing direct access would weaken cost controls, make audit harder, and create opportunities to provision unapproved resources or paid SKUs.

## Decision

Prototype Manager is the only supported route for provisioning and deleting prototype infrastructure.

Users interact with the web application, which validates requests, enforces cost-control policy, records audit events, and submits approved infrastructure changes through a restricted application identity.

## Consequences

- Users do not need Azure access for normal prototype hosting tasks.
- Approved regions, SKUs, naming conventions, and tags are enforced centrally.
- Audit history lives in the application database rather than being split across user-driven Azure actions.
- The application must keep a strong boundary between the web UI and Azure provisioning implementation.
