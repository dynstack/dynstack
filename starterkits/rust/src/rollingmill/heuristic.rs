use super::rollingmill_model::{CraneMove, Location, MoveType, PlannedCraneMoves, StackTypes, World};

pub fn next_moves(world: &World) -> PlannedCraneMoves {
    let mut schedule = PlannedCraneMoves::new();
    // in the rolling mill we got to cranes we can plan independendly.
    if !world.get_CraneMoves().get_Moves().iter().any(|mov|mov.RequiredCraneId == world.get_HandoverCrane().Id) {
        plan_handover_crane(world, &mut schedule);
    }
    if !world.get_CraneMoves().get_Moves().iter().any(|mov|mov.RequiredCraneId == world.get_ShuffleCrane().Id) {
        plan_shuffle_crane(world, &mut schedule);
    }
    schedule
}

fn arrival_stacks(world: &World) -> impl Iterator<Item=&Location>{
    world.get_Locations().iter().filter(|loc|matches!(loc.Type, StackTypes::ArrivalStack))
}
fn buffer_stacks(world: &World) -> impl Iterator<Item=&Location>{
    world.get_Locations().iter().filter(|loc|matches!(loc.Type, StackTypes::ShuffleBuffer | StackTypes::SortedBuffer))
}
fn remaining_capacity(location: &Location) -> i32{
    location.MaxHeight - size_of(location)
}
fn size_of(location: &Location) -> i32 {
    location.get_Stack().get_BottomToTop().len() as i32
}

fn plan_handover_crane(world: &World, plan: &mut PlannedCraneMoves) {
    let mut move_id = plan.get_Moves().len() as i32;
    let mut source_request: Vec<_> = world.get_MoveRequests().iter()
        .filter_map(|req|
            buffer_stacks(world)
                .find_map(|src| src.get_Stack().get_BottomToTop().iter().position(|block|block.Id==req.BlockId).map(|pos|(src, pos as i32, req)))
                
        )
        .collect();

    source_request.sort_by_key(|(src, pos, _)| size_of(src)-pos);
    
    for (src, pos, req) in source_request {
        let block = &src.get_Stack().get_BottomToTop()[pos as usize];
        let ty = block.Type;
        let mut seq = block.Sequence;
        let could_take_top_n = src.get_Stack().get_BottomToTop().iter().rev().take_while(|b|{
            let is_next = b.Type == ty && b.Sequence == seq;
            seq+=1;
            is_next
        }).count() as i32;

        let mut mov = CraneMove::new();
        move_id += 1;
        mov.Id = move_id;
        mov.Type = MoveType::PickupAndDropoff;
        mov.set_ReleaseTime(world.get_Now().clone());
        mov.PickupLocationId = src.Id;

        if could_take_top_n > 0 {
            let amount = std::cmp::min(could_take_top_n, world.get_HandoverCrane().CraneCapacity);
            mov.DropoffLocationId = req.TargetLocationId;
            mov.RequiredCraneId = world.get_HandoverCrane().Id;
            mov.Amount = amount as i32;
        } else {
            // Relocate blocks that are in the way
            let must_relocate = size_of(src) - pos - 1;
            let amount = std::cmp::min(must_relocate, world.get_HandoverCrane().CraneCapacity);
            if let Some(tgt) = buffer_stacks(world).find(|tgt|tgt.Id!=src.Id && remaining_capacity(tgt) >= amount){
                mov.DropoffLocationId = tgt.Id;
                mov.RequiredCraneId = world.get_HandoverCrane().Id;
                mov.Amount = amount as i32;
            }else{
                continue;
            }
        }
        plan.mut_Moves().push(mov);
        return;
    }    
}

fn plan_shuffle_crane(world: &World, plan: &mut PlannedCraneMoves) {
    let dont_use: Vec<_> = plan.get_Moves().iter().flat_map(|mov|vec![mov.PickupLocationId, mov.DropoffLocationId]).collect();
    let mut move_id = plan.get_Moves().len() as i32;
    let src = arrival_stacks(world).min_by_key(|loc|loc.get_Stack().get_BottomToTop().iter().map(|block|block.Sequence).min()).unwrap();

    let amount = std::cmp::min(size_of(src), world.get_ShuffleCrane().CraneCapacity);
    if amount == 0{
        return;
    }
    if let Some(tgt) = buffer_stacks(world).find(|tgt| remaining_capacity(tgt) >= amount && !dont_use.contains(&tgt.Id)) {
        let mut mov = CraneMove::new();
        move_id += 1;
        mov.Id = move_id;
        mov.Type = MoveType::PickupAndDropoff;
        mov.set_ReleaseTime(world.get_Now().clone());
        mov.PickupLocationId = src.Id;
        mov.DropoffLocationId = tgt.Id;
        mov.RequiredCraneId = world.get_ShuffleCrane().Id;
        mov.Amount = amount as i32;
        plan.mut_Moves().push(mov);
        return;
    }
}