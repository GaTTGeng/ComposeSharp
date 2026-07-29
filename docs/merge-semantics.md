# Multi-file merge semantics

`ComposeFileLoader.LoadMerged` processes files in the supplied order. The first file supplies the base document; each later file is an overlay. All paths in the resulting project, including `env_file`, are resolved from the first Compose file's directory.

This is an incremental Compose merge implementation, not full Compose Specification compatibility.

| YAML shape or field | Rule |
| --- | --- |
| Scalars, `null`, and changes between YAML shapes | Later value replaces the earlier value. |
| Mappings | Keys are merged recursively; a later scalar value for a matching key replaces the earlier value. This covers mapping-form `environment`, `labels`, build arguments, and top-level resource definitions. |
| Standard lists | Base entries are retained and overlay entries are appended. Matching scalar entries are retained once. |
| `environment` and `labels` list form | Entries are matched by the text before `=`; the later entry replaces a matching earlier entry. |
| `volumes`, `secrets`, and `configs` service lists | Entries with the same container target replace the earlier entry; entries with new targets are appended. |
| `command`, `entrypoint`, and `healthcheck.test` | The later list replaces the earlier list. |

Top-level `volumes`, `networks`, `secrets`, and `configs` are mappings, so resource names are accumulated and matching definitions merge recursively.

Compose merge tags such as `!reset` and `!override` are not supported. The loader rejects a file containing a YAML tag with an error that names that source file instead of silently applying a different interpretation. `extends`, `include`, and other Compose constructs retain their existing loader support status; this document only defines multi-file overlays.
