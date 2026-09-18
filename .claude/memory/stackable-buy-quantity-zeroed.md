---
name: stackable-buy-quantity-zeroed
description: ItemSlotContainer.TryAdd absorbs a stackable and sets the source ItemInstance.Quantity to 0; capture quantities before a trade/transfer in tests
metadata: 
  node_type: memory
  type: project
  originSessionId: d5015a60-9e8e-4e16-b98d-e472cc531da7
  modified: 2026-09-17T23:35:06.905Z
---

`ItemSlotContainer.TryAdd` (backpack) merges a stackable into existing stacks / new slots and then calls
`item.SetQuantity(0)` on the source instance; stackables also get new instance ids inside the container.

**Why:** tests that read `offer.Item.Quantity` or look up a stackable by its original `InstanceId` after a merchant
`Buy` / inventory move silently compare against 0 or -1 (pass 3 lost two test cycles to this).

**How to apply:** snapshot `delivered = offer.Item.Quantity` before the trade, compare summed quantity per
`DefinitionId`, and find stackables by definition id, never by the pre-transfer instance id. Also note the
`FinalMvpAuditTests` (EditMode) read the last `TestResults/PlayMode-results.xml`, so run PlayMode before EditMode
when the final gate order matters.
