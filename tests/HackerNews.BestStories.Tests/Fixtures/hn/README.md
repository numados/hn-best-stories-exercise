# HN fixtures

- `beststories.json`, `item-49998895.json`, `item-50000488.json`, `item-50006832.json`,
  `item-50008427.json`, `item-50019911.json`,
  `item-50006366.json` (genuine Tell HN post without `url`) — captured live from
  https://hacker-news.firebaseio.com on 2026-10-09.
- `item-deleted.json`, `item-dead.json`, `item-null-body.json`, `item-no-descendants.json` —
  synthetic edge payloads following the HN schema (live deleted/dead ids are not
  discoverable through the API); used to prove parser/mapper tolerance.
