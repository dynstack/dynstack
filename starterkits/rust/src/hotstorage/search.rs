use super::brp::{BrpState, Move};

/// Does a simple depth first search using forced moves starting from the initial BrpState.
pub fn depth_first_search(initial: BrpState) -> Vec<Move> {
    let mut budget = 10000;
    let mut best: Option<Vec<Move>> = None;
    let mut stack = vec![initial];
    let mut possible_moves = Vec::new();
    while let Some(state) = stack.pop() {
        if budget == 0 {
            break;
        }
        budget -= 1;
        state.forced_moves(&mut possible_moves);
        if state.is_solved() {
            let sol = state.into_moves();
            if best
                .as_ref()
                .map_or(true, |curr_best| curr_best.len() > sol.len())
            {
                best = Some(sol);
            }
        } else {
            for mov in &possible_moves {
                stack.push(state.apply_move(*mov));
            }
        }
    }
    best.unwrap_or_default()
}
