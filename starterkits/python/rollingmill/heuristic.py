from rollingmill.rollingmill_model_pb2 import World, PlannedCraneMoves, StackTypes, MoveType, CraneMove

def next_moves(world):
    plan = PlannedCraneMoves()
    if all(mov.RequiredCraneId != world.HandoverCrane.Id for mov in world.CraneMoves.Moves):
        plan_handover_crane(world, plan)
    if all(mov.RequiredCraneId != world.ShuffleCrane.Id for mov in world.CraneMoves.Moves):
        plan_shuffle_crane(world, plan)
    return plan

def arrival_stacks(world):
    return filter(lambda loc: loc.Type==StackTypes.ArrivalStack, world.Locations)

def buffer_stacks(world):
    return filter(lambda loc: loc.Type==StackTypes.ShuffleBuffer or loc.Type==StackTypes.SortedBuffer, world.Locations)


def size_of(location):
    return len(location.Stack.BottomToTop)

def remaining_capacity(location):
    return location.MaxHeight - size_of(location)

def position_of_block_in(loc, block_id):
    for (pos, block) in enumerate(loc.Stack.BottomToTop):
        if block.Id == block_id:
            return pos
    return None

def plan_handover_crane(world, plan):
    move_id = len(plan.Moves)
    source_request = []
    for req in world.MoveRequests:
        for src in buffer_stacks(world):
            pos = position_of_block_in(src, req.BlockId)
            if pos:
                source_request.append((src, pos, req))
            
    source_request.sort(key = lambda x: size_of(x[0])-x[1])
    
    for (src, pos, req) in source_request:
        block = src.Stack.BottomToTop[pos]
        ty = block.Type
        seq = block.Sequence
        
        could_take_top_n = 0
        for block in reversed(src.Stack.BottomToTop):
            if block.Type == ty and block.Sequence == seq:
                could_take_top_n += 1
                seq += 1
            else:
                break

        mov = CraneMove()
        move_id += 1
        mov.Id = move_id
        mov.Type = MoveType.PickupAndDropoff
        mov.ReleaseTime.MilliSeconds = world.Now.MilliSeconds
        mov.PickupLocationId = src.Id
        mov.PickupGirderPosition = src.GirderPosition

        if could_take_top_n > 0:
            amount = min(could_take_top_n, world.HandoverCrane.CraneCapacity)
            mov.DropoffLocationId = req.TargetLocationId
            mov.DropoffGirderPosition = world.Locations[req.TargetLocationId].GirderPosition
            mov.RequiredCraneId = world.HandoverCrane.Id
            mov.Amount = amount
        else:
            # Relocate blocks that are in the way
            must_relocate = size_of(src) - pos - 1
            amount = min(must_relocate, world.HandoverCrane.CraneCapacity)
            tgt = next((tgt for tgt in buffer_stacks(world) if tgt.Id!=src.Id and remaining_capacity(tgt) >= amount), None)
            if tgt:
                mov.DropoffLocationId = tgt.Id;
                mov.DropoffGirderPosition = tgt.GirderPosition;
                mov.RequiredCraneId = world.get_HandoverCrane().Id;
                mov.Amount = amount;
            else:
                continue
        
        plan.Moves.append(mov)
        return  

def plan_shuffle_crane(world, plan):
    dont_use = [loc for mov in plan.Moves for loc in (mov.PickupLocationId, mov.DropoffLocationId) ]
    move_id = len(plan.Moves)
    src = min(arrival_stacks(world), key = lambda loc: min(block.Sequence for block in loc.Stack.BottomToTop))

    amount = min(size_of(src), world.ShuffleCrane.CraneCapacity)
    if amount == 0:
        return
    tgt = next((tgt for tgt in buffer_stacks(world) if tgt.Id not in dont_use and remaining_capacity(tgt) >= amount), None)
    if tgt:
        mov = CraneMove()
        move_id += 1
        mov.Id = move_id
        mov.Type = MoveType.PickupAndDropoff
        mov.ReleaseTime.MilliSeconds = world.Now.MilliSeconds
        mov.PickupLocationId = src.Id
        mov.PickupGirderPosition = src.GirderPosition
        mov.DropoffLocationId = tgt.Id
        mov.DropoffGirderPosition = tgt.GirderPosition
        mov.RequiredCraneId = world.ShuffleCrane.Id
        mov.Amount = amount
        plan.Moves.append(mov)
        return
"""

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
        mov.PickupGirderPosition = src.GirderPosition;

        if could_take_top_n > 0 {
            let amount = std::cmp::min(could_take_top_n, world.get_HandoverCrane().CraneCapacity);
            mov.DropoffLocationId = req.TargetLocationId;
            mov.DropoffGirderPosition = world.get_Locations()[req.TargetLocationId as usize].GirderPosition;
            mov.RequiredCraneId = world.get_HandoverCrane().Id;
            mov.Amount = amount as i32;
        } else {
            // Relocate blocks that are in the way
            let must_relocate = size_of(src) - pos - 1;
            let amount = std::cmp::min(must_relocate, world.get_HandoverCrane().CraneCapacity);
            if let Some(tgt) = buffer_stacks(world).find(|tgt|tgt.Id!=src.Id && remaining_capacity(tgt) >= amount){
                mov.DropoffLocationId = tgt.Id;
                mov.DropoffGirderPosition = tgt.GirderPosition;
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
        mov.PickupGirderPosition = src.GirderPosition;
        mov.DropoffLocationId = tgt.Id;
        mov.DropoffGirderPosition = tgt.GirderPosition;
        mov.RequiredCraneId = world.get_ShuffleCrane().Id;
        mov.Amount = amount as i32;
        plan.mut_Moves().push(mov);
        return;
    }
}
"""