# Direct-well Configured Logic setup

Extends the existing setup editor and immutable revision/Asset/Data binding model.
A ForeSite-required module may select one imported well directly instead of
creating a one-well View. Views remain the existing option for reusable groups.
The editor displays the selected saved connection without another connection form.
The submitted external identity is checked against the stored SourceBinding;
settings and credentials are never editable execution inputs.

The existing paired configured-read/manage grant fence runs before the shared
source validator. Draft save, revision validation, activation and authorized
manual request preparation reuse that validator. It requires exactly one active
leaf well, one ForeSite well binding, a compatible enabled saved connection, and
matching adapter/external identities on every data binding. There is no new table,
API, permission, connection registry or View. Direct selection requires Asset/
configuration authority but does not require Connection/View browsing permission.

Acceptance extends AssetViewChecks with a real persisted direct-well draft,
validation and activation without Connection browsing permission, forged external
identity rejection and multi-well direct-selection rejection. Existing complete
View selection and scope regressions remain. Interaction tests verify a direct
selection submits the same setup endpoint without a View and retains the selected
connection identity. No live ForeSite dependency or provider writes.
