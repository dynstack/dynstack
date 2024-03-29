from cranescheduling.cranescheduling_model_pb2 import Crane, CraneSchedule, CraneScheduleActivity, CraneSchedulingSolution

def can_reach(crane, girder_position):
    return crane.MinPosition <= girder_position and girder_position <= crane.MaxPosition

def next_moves(world):
    if len(world.CraneMoves) <= 0:
        return None

    schedule = CraneSchedule()

    # create schedule with moves generated by simulation
    for item in world.CraneMoves:
        crane_id = 1 + abs(item.Id) % 2
        crane = world.Cranes[crane_id - 1]

        # fix crane assignment if necessary
        if not can_reach(crane, item.PickupGirderPosition) or not can_reach(crane, item.DropoffGirderPosition):
            crane_id = crane_id % 2 + 1

        activity = CraneScheduleActivity()
        activity.CraneId = crane_id
        activity.MoveId = item.Id

        schedule.Activities.append(activity)

    solution = CraneSchedulingSolution()
    solution.Schedule.CopyFrom(schedule)

    # custom moves could be added here
    # solution.CustomMoves.append(...)

    return solution
