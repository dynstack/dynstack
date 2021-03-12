mod brp;
mod hotstorage_model;
mod heuristic;
mod search;

use crate::OptimizerType;

pub fn plan_moves(world: &hotstorage_model::World, opt: OptimizerType) -> Option<hotstorage_model::CraneSchedule> {
    if !world.get_Crane().get_Schedule().get_Moves().is_empty() {
        // Leave the existing schedule alone
        return None;
    }
    let mut schedule = match opt {
        OptimizerType::RuleBased => heuristic::next_moves(world),
        OptimizerType::ModelBased => brp::calculate_schedule(world),
    };

    if schedule.get_Moves().is_empty() {
        // avoid sending empty schedules
        None
    } else {
        // set sequence number because the simulation only accepts Schedules with increasing sequence numbers.       
        let sequence = world.get_Crane().get_Schedule().get_SequenceNr();
        schedule.set_SequenceNr(sequence + 1);
        println!("new schedule {:?}", schedule);
        Some(schedule)
    }
}