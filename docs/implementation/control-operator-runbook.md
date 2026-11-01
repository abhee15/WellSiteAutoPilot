# Control operator runbook

What a person does with the control gate in this build. Only the **simulator** sink exists: no real equipment receives
anything, and a policy for any other sink is refused (`CONTROL_REAL_WRITES_DISABLED`). The behaviour below is what the gate
does today; see `docs/design/provider-command-seam.md` for what changes when a real provider is added.

## Who can do what

| Action | Needs |
| --- | --- |
| See commands, the kill switch and policies | `recommendations.read` (commands) or `configured-logic.read` (policy), within asset scope |
| Execute an approved recommendation, engage or release the kill switch, edit a policy, verify or resolve a command | `control.manage` (deployment-wide) **and**, for anything tied to a recommendation, the right to decide that recommendation on its asset |

A command you can see but not decide says so and offers no buttons.

## The layers a command passes

A command is sent only if every layer allows it. A refusal names the first layer that said no and sends nothing.

1. **Deployment switch** (`Control:Autonomous:Enabled`, set by an administrator). Off means every command is refused.
2. **Kill switch.** One switch for the whole deployment, re-read just before sending. Engage it to stop all commands now.
   Engaging asks only for an optional reason; releasing also needs a tick box, because it lets approved commands go again. It stops *commands*, not the
   logic: recommendations keep being produced.
3. **The configuration's autonomy policy.** It must exist and be enabled, the revision must be live and in the right mode,
   the command must be on the policy's list with a matching unit and a value inside its inclusive bounds, and the rate limit
   (actions per window) must not be used up.
4. **The recommendation.** It must be approved, not expired, and, for a person executing an approved recommendation, the
   policy must allow approved recommendations and the approval must be recent (30 minutes by default).

## Executing an approved recommendation

Open the recommendation and choose **Execute**. Tick the confirmation. The gate re-checks everything above at that moment, so a
refusal here is normal and tells you what to fix:

| Message | What to do |
| --- | --- |
| Control actions are switched off for this deployment | Ask an administrator to enable control |
| The control kill switch is engaged | Find out why it was engaged before releasing it |
| This configuration has no autonomy policy / the policy is disabled | Create or enable the policy (Logic, the configuration, Autonomy policy) |
| The autonomy policy does not permit this command | Add the command with unit and bounds, or do not run it |
| The value is outside the bounds / the unit does not match | The recommendation is outside what was agreed; do not widen bounds to fit it |
| The rate limit has been reached | Wait for the window to pass |
| The approval is too old | Run the logic again and approve the new recommendation |
| This configuration does not allow approved recommendations | Turn on "allow approved recommendations" in the policy, if that is intended |
| Advice has no device action | Nothing to execute; advice is for a person to act on |

Each attempt, allowed or refused, is recorded with who did it.

## After a command is sent

| State | Meaning | What you do |
| --- | --- | --- |
| Verified | The target was read back, or a person attested, and it holds the value | Nothing |
| Dispatched | Sent; the target has not confirmed yet | Wait, or **Verify** |
| Ambiguous | Sent, but the result is unknown (the target errored, timed out, or read back a different value) | Decide: see below |
| Failed | Not applied | Nothing, or fix the cause and run again |
| Blocked | Stopped before it was sent | Nothing was sent; see the reason |

A command is never assumed either applied or not applied when that is unknown. Commands waiting for a decision appear on
Recommendations ("Commands awaiting a decision", longest waiting first) and on the Overview.

### Verify or resolve

- **Verify** asks the target again. If it answers, the command is updated. If it gives no answer, the command stays open: say so
  and try later.
- **Resolve** is for when nobody can verify. Go and look at the equipment yourself, then record whether the command was
  applied, with a note saying what you checked. Your name and note are kept. Do not resolve from memory or from what a
  colleague said; do not resolve to make the list shorter.

Never run the same command again to "fix" an ambiguous one until you know whether the first one landed.

## Writing a policy

A policy belongs to a configuration. Fields: enabled, sink (Simulator), the commands it permits (name, unit, inclusive minimum
and maximum), the rate limit (actions per window and window length), and whether approved recommendations may be executed
by a person. Rules the editor and the server both apply: a name is permitted once; the reserved advice command cannot be
permitted; an enabled policy permits at least one command with both bounds. Start disabled, add one command with narrow
bounds, and widen only with a reason.
