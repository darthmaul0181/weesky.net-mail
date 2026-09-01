/** One group as GET /api/ContactGroups answers it. memberIds only carries resolved members, so
    counters, list filtering, chips and the composer's expansion all read one truth. */
export interface ContactGroup {
  id: string
  name: string
  memberIds: string[]
}

export interface ContactGroupsResponse {
  groups: ContactGroup[]
}
