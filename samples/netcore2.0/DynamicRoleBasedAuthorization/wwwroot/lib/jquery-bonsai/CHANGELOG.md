# v2.0

- List items are now identified by an attribute, defined by the `idAttribute` option. By default
  the `id` attribute is used.
- The serialization format has changed to `{expanded: [id...], collapsed: [id...], version: 2}`. 
  The previous format returned by `serialize()` is also supported. 
- Unnecessary entries are excluded from serialization data structure (items without ids or children need not be included).
- Checkbox ids are now sequential - if you want to address a checkbox use a selector that targets ".the-list-item > input".
- Remove the need for guid, generatedIdPrefix, specifiedIdPrefix and adding generated ids to list items.
