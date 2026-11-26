# Shared subgraph index (Other scope: compiled from all local ShaderGraph assets)

Cross-reference for the three ShaderGraph detective folders. IMPORTANT: the `.shadersubgraph.meta`
GUIDs in this converted project are Prowl UUIDs and do NOT equal the `m_SerializedSubGraph.guid`
values recorded inside the parent graphs. Identity was resolved by matching the subgraph node
`m_PropertyGuids` to each subgraph file's property `m_Guid.m_GuidSerialized` (stable) plus node name.

| subgraph file | property GUID -> name | instances (all local graphs) |
|---|---|---:|
| `Normal map` | 460b7d2e-335f-408d-b867-803fa943c37e -> normal strength; 2c05005b-b071-41ea-af9d-fe8200dbaf5e -> UV | 4 |
| `Sinewave` | 59286b15-ec24-4a08-9459-e54a3557aad6 -> position; 36172bc2-416d-4e3b-8039-99f692bb8552 -> wavelength | 2 |
| `depthfade` | 17a2e3f2-5abb-4003-a5dc-0a840934d147 -> Strength; d030c457-59de-4a36-affa-232131e40101 -> Distance | 2 |
| `skybox uv` |  | 2 |
| `stars` | f4d46276-2065-4f59-aac6-fbe265623ee5 -> noise scale; e7cb5f8b-08b6-4ead-83ce-636ec72502be -> star power; 1a676949-99a1-4f92-8023-b5f6b5745780 -> intensity | 1 |
| `texture movement` | 36cf05d1-3878-4700-8080-6d5c0c4008df -> Speed; 9ba37783-4cb3-4b26-bd2f-04384d94c461 -> Scale | 6 |

## Instance detail (graph | node id | name | (resolved param name OR unmatched GUID prefix, propertyId))

### Normal map (4 instances)
- `water.shadergraph` `0131bb1a` Normal map: [('2c05005b', -1312885115), ('460b7d2e', 1204120160)]
- `water.shadergraph` `14a6acdb` Normal map: [('2c05005b', -1312885115), ('460b7d2e', 1204120160)]
- `water.shadergraph` `5378309f` Normal map: [('2c05005b', -1312885115), ('460b7d2e', 1204120160)]
- `water.shadergraph` `a3e40612` Normal map: [('2c05005b', -1312885115), ('460b7d2e', 1204120160)]
### Sinewave (2 instances)
- `water.shadergraph` `129d00de` Sinewave: [('59286b15', -1888515696), ('36172bc2', -600774467)]
- `water.shadergraph` `ca5da90f` Sinewave: [('59286b15', -1888515696), ('36172bc2', -600774467)]
### depthfade (2 instances)
- `water.shadergraph` `5e469230` depthfade: [('d030c457', -1172012265), ('17a2e3f2', -1803922971)]
- `water.shadergraph` `66ad73dd` depthfade: [('d030c457', -1172012265), ('17a2e3f2', -1803922971)]
### skybox uv (2 instances)
- `Skybox.shadergraph` `1a6c53b0` skybox uv: []
- `stars.shadersubgraph` `cd9f8974` skybox uv: []
### stars (1 instances)
- `Skybox.shadergraph` `9b8bc176` stars: [('0fccdf9e', -2097500440), ('f4d46276', -1136072996), ('e7cb5f8b', 2030550137), ('1a676949', 588752861), ('9480971c', 1819872489)]
### texture movement (6 instances)
- `Heat Distortion.shadergraph` `42663009` texture movement: [('Scale', -1991933760), ('Speed', -223675099), ('15ab56ef', -1873376002)]
- `water.shadergraph` `6c54bdc7` texture movement: [('c2c894f2', 2130692703), ('Scale', -1991933760), ('Speed', -223675099), ('15ab56ef', -1873376002)]
- `water.shadergraph` `6ec45782` texture movement: [('c2c894f2', 2130692703), ('Scale', -1991933760), ('Speed', -223675099), ('15ab56ef', -1873376002)]
- `water.shadergraph` `8ff6f95c` texture movement: [('c2c894f2', 2130692703), ('Scale', -1991933760), ('Speed', -223675099), ('15ab56ef', -1873376002)]
- `water.shadergraph` `bee82d22` texture movement: [('c2c894f2', 2130692703), ('Scale', -1991933760), ('Speed', -223675099), ('15ab56ef', -1873376002)]
- `water.shadergraph` `f02cc53e` texture movement: [('Scale', -1991933760), ('Speed', -223675099), ('15ab56ef', -1873376002)]

### Other-scope usage (the five graphs assigned to this folder)

- `texture movement`: Heat Distortion.shadergraph#42663009 ([('Scale', -1991933760), ('Speed', -223675099), ('15ab56ef', -1873376002)])
