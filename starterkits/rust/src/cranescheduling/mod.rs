mod cranescheduling_model;

use cranescheduling_model::{Crane, CraneSchedule, CraneScheduleActivity, CraneSchedulingSolution};
use std::sync::atomic::{AtomicI32, Ordering};

static SCHEDULE_NR: AtomicI32 = AtomicI32::new(0);

fn can_reach(crane: &Crane, girder_position: f64) -> bool {
    crane.MinPosition <= girder_position && girder_position <= crane.MaxPosition
}

pub fn plan_moves(
    world: &cranescheduling_model::World,
) -> Option<cranescheduling_model::CraneSchedulingSolution> {
    let moves = world.get_CraneMoves();
    let move_count = moves.len() as i32;

    if move_count <= 0 {
        return None;
    }

    let mut schedule = CraneSchedule::new();
    SCHEDULE_NR.fetch_add(1, Ordering::SeqCst);
    let schedule_nr = SCHEDULE_NR.load(Ordering::SeqCst);
    schedule.set_ScheduleNr(schedule_nr);

    for m in moves {
        let mut crane_id = 1 + m.Id.abs() % 2;
        let crane = &world.get_Cranes()[(crane_id - 1) as usize];

        // fix crane assignment if necessary
        if !can_reach(crane, m.PickupGirderPosition) || !can_reach(crane, m.DropoffGirderPosition) {
            crane_id = crane_id % 2 + 1;
        }

        let mut activity = CraneScheduleActivity::new();
        activity.set_CraneId(crane_id);
        activity.set_MoveId(m.Id);

        schedule.mut_Activities().push(activity);
    }

    let mut solution = CraneSchedulingSolution::new();
    solution.set_Schedule(schedule);

    // custom moves could be added here
    // solution.mut_CustomMoves().push(...)

    return Some(solution);
}
