# Incomplete operation recovery

## Purpose

Backup preparation, Apply, post-Apply verification, and rollback can all be interrupted by:

* app close
* crash
* cancellation
* partial filesystem failure

The app must not hide these operations.

## Detection model

On the next launch, operation history is scanned for incomplete states such as:

* backup still pending, copying, verifying, or failed before a clean package exists
* Apply marked `InProgress` or `Cancelled`
* completed Apply missing post-Apply verification
* rollback marked `InProgress`, `Cancelled`, or partially completed

## Recovery actions

The UI surfaces these actions:

* `Re-verify`
* `Continue verification`
* `Roll back`
* `View details`

Each action stays narrow:

* `Re-verify` re-checks the saved package
* `Continue verification` re-runs post-Apply verification from persisted operation data
* `Roll back` reuses the existing guarded rollback workflow
* `View details` focuses the saved operation package and summary

## Safety rules

Recovery must not:

* invent new write targets
* force overwrite conflicts by default
* skip verification after partial work
* silently clear incomplete records

## Expected user experience

The user should always be able to see that something incomplete happened and understand what the next safe action is.

At minimum, the app should report:

* which stage was interrupted
* whether rollback is available
* whether more verification is required
* where to inspect the saved operation package
