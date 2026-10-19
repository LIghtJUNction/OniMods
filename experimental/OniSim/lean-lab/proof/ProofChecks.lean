import SimDiamond

-- Report every theorem's logical dependencies. No native_decide is used.
#print axioms SimDiamond.Walk.start_legal
#print axioms SimDiamond.Walk.end_legal
#print axioms SimDiamond.adjacent_distance
#print axioms SimDiamond.Walk.distance_le
#print axioms SimDiamond.step_towards
#print axioms SimDiamond.shortest_walk
#print axioms SimDiamond.reach_iff_diamond
#print axioms SimDiamond.mem_intRange
#print axioms SimDiamond.nodup_intRange
#print axioms SimDiamond.row_intervals_iff_diamond
#print axioms SimDiamond.mem_scan_iff_intervals
#print axioms SimDiamond.mem_scan_iff_diamond
#print axioms SimDiamond.mem_scan_iff_reach
#print axioms SimDiamond.nodup_row
#print axioms SimDiamond.row_member_y
#print axioms SimDiamond.nodup_scan
#print axioms SimDiamond.scan_zero
#print axioms SimDiamond.checkedScan_illegal
#print axioms SimDiamond.checkedScan_zero
#print axioms SimDiamond.mem_checkedScan_iff_reach
#print axioms SimDiamond.mem_checkedScan_iff_diamond
#print axioms SimDiamond.nodup_checkedScan
#print axioms SimDiamond.mem_neighbors_iff
#print axioms SimDiamond.mem_frontier_iff_walk
#print axioms SimDiamond.mem_unique_iff
#print axioms SimDiamond.nodup_unique
#print axioms SimDiamond.mem_bfs_iff_reach
#print axioms SimDiamond.nodup_bfs
#print axioms SimDiamond.bfs_checkedScan_same_cells
#print axioms SimDiamond.runUpdates_at
#print axioms SimDiamond.runUpdates_same_set
#print axioms SimDiamond.bfs_checkedScan_same_updates
#print axioms SimDiamond.linearIndex_injective
#print axioms SimDiamond.legal_effectiveBounds

-- The signatures expose the assumptions of the three main results.
#check SimDiamond.reach_iff_diamond
#check SimDiamond.bfs_checkedScan_same_cells
#check SimDiamond.bfs_checkedScan_same_updates
#check SimDiamond.linearIndex_injective
