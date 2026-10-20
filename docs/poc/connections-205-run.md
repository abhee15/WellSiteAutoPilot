# Running the connections POC

1. Postgres (any instance), NATS on 4222, and ForeSite on `http://localhost` (Windows auth) pointed at the desired database.
2. Start Migrator, ProviderSimulator (5091), Integration Gateway (5081), Worker (5082) and Server (5080) with `dotnet run`. Set:
   - `ConnectionStrings` to your Postgres
   - `Connections__StorePath=<path>\connections.json` (Gateway)
   - `Security__AuthenticationMode=TestHeader` (Server, dev only)
   - `ASPNETCORE_ENVIRONMENT=Development`
3. Web: in `src/web`, run `npm run dev` with `WSA_DEV_AUTH_MODE=TestHeader`, `WSA_DEV_TEST_IDENTITY=LOCAL\wsa-dev-operator`, `WSA_DEV_API_TARGET=http://127.0.0.1:5080`.
4. Open Administration > Connections, add a ForeSite connection (base URL), Test, Discover, choose a template, Apply.
5. Adapter tests: `FORESITE_BASE_URL=http://localhost dotnet run --project tests/WellSiteAutoPilot.ForeSiteAdapterTests`.

The local launcher used for the POC (Aspire's DCP did not work on the POC box) is not committed.
