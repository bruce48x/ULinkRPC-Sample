# Production Infra Plan

## Workflow Rule

For this repository, any new feature request should follow this order:

1. update or create a design document
2. update or create a development plan document
3. only then start implementation

## This Change

### Phase 1: Compose Infrastructure

- add root `docker-compose.yml`
- start `postgres` with persistent volume and Orleans init SQL
- start `redis` with persistent volume and password
- add `.env.example` for local bootstrap

### Phase 2: Orleans Persistence Integration

- remove `UseLocalhostClustering`
- switch `Server/Silo` to ADO.NET clustering
- replace `AddMemoryGrainStorage(...)` with `AddAdoNetGrainStorage(...)`
- switch `Server/Server` Orleans client to ADO.NET clustering

### Phase 3: Configuration Externalization

- add `appsettings.json` for `Server/Server`
- add `appsettings.json` for `Server/Silo`
- expose cluster id, service id, db connection string, invariant, and ports through configuration

### Phase 4: Validation

- restore NuGet packages
- build `Server/Silo`
- build `Server/Server`
- confirm no compile regressions from the Orleans provider changes

## Acceptance Criteria

- `docker compose up -d` can start PostgreSQL and Redis
- PostgreSQL initializes Orleans required schema automatically
- `Server/Silo` no longer depends on in-memory grain storage
- `Server/Server` no longer depends on localhost clustering
- both server-side executables build successfully
- design doc explicitly states that gateway realtime state is still single-node for now
