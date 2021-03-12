mod rollingmill_model;
mod heuristic;

pub fn plan_moves(world: &rollingmill_model::World) -> Option<rollingmill_model::PlannedCraneMoves> {
    let mut planned_moves = heuristic::next_moves(world);

    // set sequence number because the simulation only accepts Schedules with increasing sequence numbers.       
    let sequence = world.get_CraneMoves().get_SequenceNr();
    planned_moves.set_SequenceNr(sequence + 1);
    if planned_moves.get_Moves().is_empty() {
        // avoid sending empty schedules
        None
    } else {
        Some(planned_moves)
    }
}