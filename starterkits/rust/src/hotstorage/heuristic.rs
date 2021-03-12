use super::hotstorage_model::{CraneMove, CraneSchedule, World};

/// Use simple heuristics to come up with a crane schedule.
pub fn next_moves(world: &World) -> CraneSchedule {
    let mut schedule = CraneSchedule::new();
    any_handover_move(world, &mut schedule);
    clear_production_stack(world, &mut schedule);
    schedule
}

/// If any block on top of a stack can be moved to the handover schedule this move.
fn any_handover_move(world: &World, schedule: &mut CraneSchedule) {
    if !world.get_Handover().get_Ready() {
        return;
    }
    for stack in world.get_Buffers() {
        if let Some(top) = stack.get_BottomToTop().last() {
            if top.get_Ready() {
                let mut mov = CraneMove::new();
                mov.set_BlockId(top.get_Id());
                mov.set_SourceId(stack.get_Id());
                mov.set_TargetId(world.get_Handover().get_Id());
                schedule.mut_Moves().push(mov);
                return;
            }
        }
    }
}

/// If the top block of the production stack can be put on a buffer schedule this move.
fn clear_production_stack(world: &World, schedule: &mut CraneSchedule) {
    if let Some(block) = world.get_Production().get_BottomToTop().last() {
        if let Some(free) = world
            .get_Buffers()
            .iter()
            .find(|b| (b.get_MaxHeight() as usize) > b.get_BottomToTop().len())
        {
            let mut mov = CraneMove::new();
            mov.set_BlockId(block.get_Id());
            mov.set_SourceId(world.get_Production().get_Id());
            mov.set_TargetId(free.get_Id());
            schedule.mut_Moves().push(mov);
        }
    }
}
