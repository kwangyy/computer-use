// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

//! Exact Windows wire abbreviations. Decoding stays within the owned Rust graph.

use super::*;

impl<'a> WireValues<'a> {
    pub(super) fn open_windows_pools(&mut self) -> WireResult<()> {
        if self.platform != "windows" && self.raw.get("propertyPool").is_some() {
            return Err(WireError::Invalid("driver observation: property pools are Windows-only"));
        }
        if let Some(entries) = self.raw.get("propertyPool") {
            let entries = array(entries)?;
            if entries.len() > NODE_BUDGET { return Err(BUDGET_ERROR); }
            for entry in entries {
                // Even unused entries must be valid, with no nested pool or object references.
                let mut pending = vec![entry];
                while let Some(value) = pending.pop() {
                    self.debit(0)?;
                    if let Some(fields) = value.as_map() {
                        if value.get("ref").is_some() || value.get("property").is_some() {
                            return Err(WireError::Invalid("driver observation: pooled properties must be reference-free inline records"));
                        }
                        pending.extend(fields.iter().map(|(_, value)| value));
                    } else if let Some(items) = value.as_seq() {
                        pending.extend(items);
                    }
                }
                let full = self.inline_property(entry, false)?.into_owned();
                let query = self.inline_property(entry, true)?.into_owned();
                let size = owned_size(entry).saturating_add(owned_size(&full.read));
                self.property_pool.push((full, query, size));
            }
        }
        Ok(())
    }

    pub(crate) fn property(&mut self, raw: &'a Value, query_only: bool) -> WireResult<WireProperty<'a>> {
        if let Some(index) = raw.get("property") {
            self.debit(0)?;
            record(raw, &["property"], &[])?;
            let index = index.as_i64().and_then(|index| usize::try_from(index).ok())
                .filter(|index| *index < self.property_pool.len())
                .ok_or(WireError::Invalid("driver observation: invalid property pool index"))?;
            let (full, query, size) = &self.property_pool[index];
            self.metadata_expansion_remaining = self.metadata_expansion_remaining.checked_sub(*size)
                .ok_or(WireError::Unsupported("property expansion limit"))?;
            return Ok(if query_only { query } else { full }.clone());
        }
        self.inline_property(raw, query_only)
    }
}
